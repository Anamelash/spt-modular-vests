using System.Text.Json;
using ModularVests.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ModularVests.Server.Services;

/// <summary>
/// Turns <see cref="ItemsConfig"/> into database content: pouch templates, rig templates
/// (with their default presets) and the rows of the mod's trader that sell them.
/// Idempotent: an id that already exists is left alone.
/// </summary>
[Injectable]
public class ModularItemsRegistrar(
    CustomItemService customItemService,
    TemplateTable templateTable,
    TradersTable tradersTable,
    GlobalTable globalTable,
    TraderRegistrar traderRegistrar,
    LocaleTable localeTable,
    ICloner cloner,
    ISptLogger<ModularItemsRegistrar> logger)
{
    /// <summary>Ordinary mod slot (mod_equipment on helmets), NOT the plate prototype.</summary>
    public const string ModSlotProto = "55d30c4c4bdc2db4468b457e";

    /// <summary>Prototype of every container grid.</summary>
    public const string GridProto = "55d329c24bdc2d892f8b4567";

    public const string RoublesTpl = TraderRegistrar.RoublesTpl;

    /// <summary>Registers everything; returns a one-line summary, or null when nothing was done.</summary>
    public string? Register(ItemsConfig config)
    {
        var items = templateTable.Items;

        // A rig on an optional donor (a colour from another mod) is left out without it; the
        // summary line counts them.
        var all = config.AllVests();
        var vests = all.Where(v => !v.Optional || items.ContainsKey(new MongoId(v.CloneTpl))).ToList();
        var skipped = all.Count - vests.Count;

        // A missing donor means a missing dependency (WTT-ContentBackport): register nothing
        // rather than half a line - a pouch without a rig to put it on is only clutter.
        var missing = vests.Select(v => v.CloneTpl)
            .Concat(config.Pouches.Select(p => p.CloneTpl))
            .Where(tpl => !items.ContainsKey(new MongoId(tpl)))
            .Distinct()
            .ToList();
        if (missing.Count > 0)
        {
            logger.Error("[ModularVests] donor template(s) not found: " + string.Join(", ", missing) +
                         ". Is WTT-ContentBackport installed and enabled? No items registered");
            return null;
        }

        var pouches = config.AllPouches();
        var pouchTpls = pouches.ToDictionary(
            p => p.Key, p => DeterministicId.For(DeterministicId.PouchKey(p.Key)));

        // The trader comes from TraderRegistration; without him the line-up is registered but not sold.
        var traderId = traderRegistrar.TraderId;
        if (traderId == null)
        {
            logger.Error("[ModularVests] the mod's trader is not registered, the items are not sold");
        }

        var rigs = 0;
        var sold = new List<MongoId>();

        foreach (var pouch in pouches)
        {
            RegisterPouch(pouch, pouchTpls[pouch.Key]);
            AddAssort(traderId, DeterministicId.PouchKey(pouch.Key), pouchTpls[pouch.Key], pouch.Price,
                pouch.LoyaltyLevel);
            sold.Add(new MongoId(pouchTpls[pouch.Key]));
        }

        foreach (var vest in vests)
        {
            var tpl = DeterministicId.For(DeterministicId.VestKey(vest.Key));
            if (RegisterVest(config, vest, tpl, pouchTpls) == null)
            {
                continue;
            }

            rigs++;
            // on the idempotent path too: the preset may not have existed in an earlier version
            RegisterPreset(vest, tpl);

            AddAssort(traderId, DeterministicId.VestKey(vest.Key), tpl, vest.Price, vest.LoyaltyLevel);
            sold.Add(new MongoId(tpl));
            if (items.TryGetValue(new MongoId(tpl), out var registered))
            {
                sold.AddRange(InsertsOf(registered));
            }
        }

        traderRegistrar.SetBoughtItems(sold);
        RegisterSlotNames(config);

        return $"{rigs} rig(s), {pouches.Count} pouch(es), skipped: {skipped}";
    }

    /// <summary>
    /// Everything a rig's slots accept: its soft armor panels, collar, shoulders and groin, and
    /// every plate that fits it.
    ///
    /// The trader has to buy these as well as the rig itself. A rig is never a bare root - its
    /// panels are in required slots and its plates usually are in too - and the game only lets
    /// a trader buy an item when it buys every part inside it as well
    /// (<c>CanBuyItem</c> walks <c>GetAllItems()</c>). Without the parts on the list the rig is
    /// simply unsellable, which is what the mod shipped with.
    /// </summary>
    internal static IEnumerable<MongoId> InsertsOf(TemplateItem template) =>
        (template.Properties?.Slots ?? [])
        .SelectMany(slot => slot.Properties?.Filters ?? [])
        .SelectMany(filter => (filter.Filter ?? []).Concat(
            filter.Plate is { } plate && !plate.IsEmpty ? [plate] : Array.Empty<MongoId>()))
        .Distinct();

    /// <summary>
    /// The inspect window writes a slot's name over an empty slot, localised by the slot name
    /// (the game's own are upper case: "MOD_MAGAZINE"); without an entry a cell read "MOD_POUCH_3".
    /// Every cell of every rig gets the configured name, in both spellings.
    /// </summary>
    private void RegisterSlotNames(ItemsConfig config)
    {
        if (config.PouchSlotName.Count == 0)
        {
            return;
        }

        var cells = config.AllVests().Select(v => v.Clusters).DefaultIfEmpty(0).Max() * ClusterGrid.CellsPerCluster;
        var names = Enumerable.Range(1, Math.Max(0, cells)).Select(n => ClusterGrid.SlotName(n)).ToList();
        foreach (var (language, locale) in localeTable.Global)
        {
            var text = config.PouchSlotNameFor(language);
            if (text == null)
            {
                continue;
            }

            locale.AddTransformer(data =>
            {
                if (data == null)
                {
                    return data;
                }

                foreach (var name in names)
                {
                    data[name] = text;
                    data[name.ToUpperInvariant()] = text;
                }

                return data;
            });
        }
    }


    /// <returns>true when created, false when it already existed.</returns>
    private bool RegisterPouch(PouchConfig pouch, string tpl)
    {
        var grids = pouch.Grids.Select(section => new Grid
        {
            Name = section.Name,
            Id = DeterministicId.For(DeterministicId.PouchGridKey(pouch.Key, section.Name)),
            Parent = tpl,
            Prototype = GridProto,
            Properties = new GridProperties
            {
                CellsH = section.Width,
                CellsV = section.Height,
                MinCount = 0,
                MaxCount = 0,
                MaxWeight = 0,
                IsSortingTable = false,
                Filters =
                [
                    new GridFilter
                    {
                        Filter = pouch.GridFilter.Select(id => new MongoId(id)).ToHashSet(),
                        ExcludedFilter = pouch.GridExcludedFilter.Select(id => new MongoId(id)).ToHashSet(),
                    },
                ],
            },
        }).ToList();

        return Create(new NewItemFromCloneDetails
        {
            ItemTplToClone = new MongoId(pouch.CloneTpl),
            ParentId = new MongoId(pouch.Parent),
            NewId = new MongoId(tpl),
            NewItemName = pouch.Name,
            HandbookParentId = pouch.HandbookParent,
            HandbookPriceRoubles = pouch.Price,
            FleaPriceRoubles = pouch.Price,
            AddToWeaponShelf = false,
            Locales = ToLocales(pouch.Locales),
            OverrideProperties = new TemplateItemProperties
            {
                Name = pouch.Name,
                ShortName = pouch.Name,
                Description = pouch.Name,
                // null leaves the donor's model
                Prefab = string.IsNullOrEmpty(pouch.Prefab) ? null : new Prefab { Path = pouch.Prefab, Rcid = pouch.Rcid },
                Width = pouch.Width,
                Height = pouch.Height,
                Weight = pouch.Weight,
                ExaminedByDefault = true,
                CanPutIntoDuringTheRaid = true,
                Grids = grids,
            },
        }, $"pouch '{pouch.Key}'");
    }

    /// <returns>true when created, false when it already existed, null when it failed.</returns>
    private bool? RegisterVest(ItemsConfig config, VestConfig vest, string tpl,
        IReadOnlyDictionary<string, string> pouchTpls)
    {
        var source = templateTable.Items[new MongoId(vest.CloneTpl)];

        TemplateItemProperties overrides;
        try
        {
            overrides = vest.Overrides.Deserialize<TemplateItemProperties>() ?? new TemplateItemProperties();
        }
        catch (JsonException ex)
        {
            logger.Error($"[ModularVests] rig '{vest.Key}': bad overrides: {ex.Message}");
            return null;
        }

        // The donor's own slots (plates, soft inserts, collar) move over as they are, re-keyed
        // to the new template. The client builds ArmorSlots from their plate prototype, and
        // the Vest constructor adds the armor holder for them.
        var slots = new List<Slot>();
        foreach (var donorSlot in source.Properties?.Slots ?? [])
        {
            var slot = cloner.Clone(donorSlot)!;
            slot.Id = new MongoId(DeterministicId.For(DeterministicId.VestSlotKey(vest.Key, slot.Name!)));
            slot.Parent = new MongoId(tpl);
            slots.Add(slot);
        }

        // Pouch cells, cluster by cluster. What a cell accepts follows from its position: every
        // pouch whose footprint fits from there. Overlaps between cells are the client's
        // business (native slot blocking); the server stores what it is given.
        // A cell no pouch fits from keeps its slot with an empty filter: it accepts nothing, but
        // its id stays (a later, smaller pouch fills it) and the pouches covering it block it.
        var filters = new Dictionary<int, HashSet<MongoId>>();
        for (var position = 1; position <= ClusterGrid.CellsPerCluster; position++)
        {
            filters[position] = config.PouchesAt(position).Select(p => new MongoId(pouchTpls[p.Key])).ToHashSet();
        }

        if (filters[1].Count == 0)
        {
            logger.Error($"[ModularVests] rig '{vest.Key}': no pouch in the line-up fits any cell, " +
                         "the rig is not registered");
            return null;
        }

        var closed = filters.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        if (closed.Count > 0)
        {
            logger.Info($"[ModularVests] rig '{vest.Key}': no pouch in the line-up is anchored in cell(s) " +
                        $"{string.Join(", ", closed)} of a cluster, they only get covered");
        }

        for (var number = 1; number <= vest.Clusters * ClusterGrid.CellsPerCluster; number++)
        {
            var name = ClusterGrid.SlotName(number);
            ClusterGrid.TryParse(name, out _, out var position);
            slots.Add(new Slot
            {
                Name = name,
                Id = new MongoId(DeterministicId.For(DeterministicId.VestSlotKey(vest.Key, name))),
                Parent = new MongoId(tpl),
                Prototype = ModSlotProto,
                Required = false,
                MergeSlotWithChildren = false,
                Properties = new SlotProperties
                {
                    Filters =
                    [
                        new SlotFilter
                        {
                            Shift = 0,
                            Filter = filters[position].ToHashSet(),
                        },
                    ],
                },
            });
        }

        overrides.Name = vest.Name;
        overrides.ShortName = vest.Name;
        overrides.Description = vest.Name;
        overrides.Slots = slots;
        overrides.Grids = [];

        // A recoloured rig has a bundle of its own; without one it wears the donor's model.
        if (!string.IsNullOrEmpty(vest.Prefab))
        {
            overrides.Prefab = new Prefab { Path = vest.Prefab, Rcid = vest.Rcid };
        }

        var created = Create(new NewItemFromCloneDetails
        {
            ItemTplToClone = source.Id,
            ParentId = new MongoId(vest.Parent),
            NewId = new MongoId(tpl),
            NewItemName = vest.Name,
            HandbookParentId = vest.HandbookParent,
            HandbookPriceRoubles = vest.Price,
            FleaPriceRoubles = vest.Price,
            AddToWeaponShelf = false,
            Locales = ToLocales(vest.Locales),
            OverrideProperties = overrides,
        }, $"rig '{vest.Key}'");

        if (created)
        {
            CheckPlateSlots(vest, tpl, source);
        }

        return created;
    }

    /// <summary>The clone must keep the donor's armor slots intact: prototype and default plate.</summary>
    private void CheckPlateSlots(VestConfig vest, string tpl, TemplateItem source)
    {
        var clone = templateTable.Items[new MongoId(tpl)];
        var cloneSlots = clone.Properties?.Slots?.ToDictionary(s => s.Name!) ?? [];
        foreach (var donor in source.Properties?.Slots ?? [])
        {
            if (!cloneSlots.TryGetValue(donor.Name!, out var copy) ||
                copy.Prototype != donor.Prototype ||
                copy.Properties?.Filters?.FirstOrDefault()?.Plate !=
                donor.Properties?.Filters?.FirstOrDefault()?.Plate)
            {
                logger.Warning($"[ModularVests] rig '{vest.Key}': slot '{donor.Name}' differs from the donor after cloning");
            }
        }
    }

    private bool Create(NewItemFromCloneDetails details, string what)
    {
        var result = customItemService.CreateItemFromClone(details);
        if (result.Success == true)
        {
            logger.Debug($"[ModularVests] {what} registered as {details.NewId}");
            return true;
        }

        if (templateTable.Items.ContainsKey(details.NewId))
        {
            // CreateItemFromClone refuses an existing id; that is the idempotent path, not an error
            logger.Debug($"[ModularVests] {what} already present ({details.NewId})");
            return false;
        }

        throw new InvalidOperationException(
            $"[ModularVests] {what}: clone failed: {string.Join("; ", result.Errors ?? [])}");
    }

    private void AddAssort(MongoId? traderId, string itemKey, string tpl, double price, int loyaltyLevel)
    {
        if (traderId == null)
        {
            return;
        }

        if (!tradersTable.TryGetValue(traderId.Value, out var trader) || trader?.Assort == null)
        {
            logger.Warning($"[ModularVests] trader {traderId} has no assort, {itemKey} is not sold");
            return;
        }

        var assortId = new MongoId(DeterministicId.For(DeterministicId.AssortKey(itemKey)));
        var assort = trader.Assort;
        if (assort.Items?.Any(i => i.Id == assortId) == true)
        {
            return;
        }

        assort.Items?.Add(new Item
        {
            Id = assortId,
            Template = new MongoId(tpl),
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd { StackObjectsCount = 999999, UnlimitedCount = true },
        });

        // sold assembled: the same parts the default preset lists
        foreach (var (slot, insert) in InsertsOf(itemKey, new MongoId(tpl)))
        {
            assort.Items?.Add(new Item
            {
                Id = new MongoId(DeterministicId.For(DeterministicId.AssortSlotKey(itemKey, slot))),
                Template = insert,
                ParentId = assortId.ToString(),
                SlotId = slot,
            });
        }

        assort.BarterScheme![assortId] =
        [
            [new BarterScheme { Count = price, Template = new MongoId(RoublesTpl) }],
        ];
        assort.LoyalLevelItems![assortId] = loyaltyLevel;
    }

    /// <summary>
    /// The rig's default preset (globals.ItemPresets, non-empty _encyclopedia). The flea market,
    /// Fence, loot and the handbook build armor from default presets only; an item without one
    /// is generated as a bare root - for a rig that means no soft armor and no collar.
    /// The required slots and the default plates, as vanilla armor presets have them (Fence
    /// looks for the plates to wear them down); no pouches.
    /// </summary>
    /// <returns>true when created, false when it already existed.</returns>
    private bool RegisterPreset(VestConfig vest, string tpl)
    {
        var presetId = new MongoId(DeterministicId.For(DeterministicId.PresetKey(vest.Key)));
        if (globalTable.ItemPresets.ContainsKey(presetId))
        {
            return false;
        }

        var rootId = new MongoId(DeterministicId.For(DeterministicId.PresetRootKey(vest.Key)));
        var items = new List<Item> { new() { Id = rootId, Template = new MongoId(tpl) } };
        foreach (var (slot, insert) in InsertsOf(DeterministicId.PresetKey(vest.Key), new MongoId(tpl), withPlates: true))
        {
            items.Add(new Item
            {
                Id = new MongoId(DeterministicId.For(DeterministicId.PresetSlotKey(vest.Key, slot))),
                Template = insert,
                ParentId = rootId.ToString(),
                SlotId = slot,
            });
        }

        globalTable.ItemPresets[presetId] = new Preset
        {
            Id = presetId,
            Type = "Preset",
            ChangeWeaponName = false,
            Name = $"{vest.Name}_default",
            Parent = rootId,
            Items = items,
            Encyclopedia = new MongoId(tpl),
        };
        logger.Debug($"[ModularVests] rig '{vest.Key}': default preset registered as {presetId}");
        return true;
    }

    private List<(string Slot, MongoId Tpl)> InsertsOf(string what, MongoId tpl, bool withPlates = false)
    {
        if (!templateTable.Items.TryGetValue(tpl, out var template))
        {
            return [];
        }

        var result = BuiltInInserts.For(template, withPlates);
        foreach (var slot in result.MissingSlots)
        {
            logger.Warning($"[ModularVests] {what}: slot '{slot}' is required but has no default part, " +
                           "the item comes without it");
        }

        return result.Inserts;
    }

    private static Dictionary<string, LocaleDetails> ToLocales(Dictionary<string, LocaleText> locales) =>
        locales.ToDictionary(kv => kv.Key, kv => new LocaleDetails
        {
            Name = kv.Value.Name,
            ShortName = kv.Value.ShortName,
            Description = kv.Value.Description,
        });
}
