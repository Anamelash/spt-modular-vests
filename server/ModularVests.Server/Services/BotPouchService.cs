using System.Collections.Concurrent;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Bot;

namespace ModularVests.Server.Services;

/// <summary>A pouch on a bot's rig: its cells, and whether magazines belong in it first.</summary>
public sealed record BotPouch(BotInventoryContainerService.ContainerDetails Details, bool ForRifleMags);

/// <summary>
/// The bot half at work: hangs pouches on a modular rig a bot has been given, and puts the
/// bot's magazines, ammo, meds and loot into them.
///
/// The game's own container service keeps one container per equipment slot and reads its grids
/// from the item's template; our rigs have no grids of their own, so a bot in one would carry
/// nothing at all. This keeps a set of containers per bot instead - one per pouch - and fills
/// them by the same rules, except that every section of a pouch is asked, not only the first.
///
/// Bots are generated in parallel, so everything per bot lives in a concurrent map keyed by the
/// bot's id, and the entry is dropped when its inventory is finished.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BotPouchService(
    ModConfigs configs,
    ItemHelper itemHelper,
    InventoryHelper inventoryHelper,
    BotGeneratorHelper botGeneratorHelper,
    TemplateTable templateTable,
    ISptLogger<BotPouchService> logger)
{
    private readonly ConcurrentDictionary<MongoId, List<BotPouch>> _pouches = new();
    private Dictionary<MongoId, string>? _vestKeys;
    private Dictionary<string, bool>? _forRifleMags;

    /// <summary>The rig of the line-up this template is, or null when it is not one of ours.</summary>
    public string? VestKeyOf(MongoId tpl)
    {
        var items = configs.Items;
        if (items == null)
        {
            return null;
        }

        _vestKeys ??= items.AllVests().ToDictionary(v => ModConfigs.VestTpl(v.Key), v => v.Key);
        return _vestKeys.GetValueOrDefault(tpl);
    }

    /// <summary>
    /// The pouches this bot's rig carries, hung on the first call and remembered after it.
    /// Empty when the rig is not one of ours or the bot half is switched off.
    /// </summary>
    public List<BotPouch> PouchesOf(MongoId botId, Item rig, BotBaseInventory inventory, string? botRole) =>
        _pouches.GetOrAdd(botId, _ => Outfit(rig, inventory, botRole));

    /// <summary>Forgets a bot: its inventory is finished and nothing more will be put in it.</summary>
    public void Forget(MongoId botId) => _pouches.TryRemove(botId, out _);

    private List<BotPouch> Outfit(Item rig, BotBaseInventory inventory, string? botRole)
    {
        var items = configs.Items;
        var outfitter = configs.Outfitter;
        var vestKey = VestKeyOf(rig.Template);
        if (items == null || outfitter == null || vestKey == null || inventory.Items == null)
        {
            return [];
        }

        _forRifleMags ??= items.AllPouches().ToDictionary(p => p.Key, p => p.IsRifleMagPouch);

        var vest = items.AllVests().First(v => v.Key == vestKey);
        var pouches = new List<BotPouch>();

        // A Random of its own: bots are generated on several threads at once.
        foreach (var placement in outfitter.Outfit(vest, new Random()))
        {
            var tpl = ModConfigs.PouchTpl(placement.PouchKey);
            if (!templateTable.Items.TryGetValue(tpl, out var template))
            {
                continue;
            }

            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                ParentId = rig.Id,
                SlotId = placement.SlotName,
                // Never null: the role is declared optional but the method walks it into
                // GetBotEquipmentRole, which calls ToLower() on it. An empty role reads the same
                // as none - the settings it would look up (durability, resources, lights) belong
                // to items a pouch is not.
                Upd = botGeneratorHelper.GenerateExtraPropertiesForItem(template, botRole ?? ""),
            };
            inventory.Items.Add(item);
            pouches.Add(new BotPouch(
                new BotInventoryContainerService.ContainerDetails(template, item),
                _forRifleMags.GetValueOrDefault(placement.PouchKey)));
        }

        if (logger.IsLogEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            logger.Debug($"[ModularVests] {botRole ?? "bot"} in {vestKey}: " +
                         string.Join(", ", pouches.Select(p =>
                             $"{p.Details.ContainerInventoryItem.SlotId}={p.Details.ContainerDbItem.Name}")));
        }

        return pouches;
    }

    /// <summary>
    /// Tries to put an item and its children into one of the bot's pouches. Magazines and ammo
    /// go to the magazine pouches first so that the first magazine does not eat the utility
    /// pouch; everything else goes to them last.
    /// </summary>
    public bool TryStore(MongoId botId, Item rig, BotBaseInventory inventory, string? botRole,
        MongoId rootItemId, MongoId rootItemTplId, List<Item> itemWithChildren)
    {
        var pouches = PouchesOf(botId, rig, inventory, botRole);
        if (pouches.Count == 0 || inventory.Items == null || itemWithChildren.Count == 0)
        {
            return false;
        }

        var wantsMagazinePouch = itemHelper.IsOfBaseclasses(rootItemTplId,
            [BaseClasses.MAGAZINE, BaseClasses.AMMO, BaseClasses.AMMO_BOX]);
        var (width, height) = inventoryHelper.GetItemSize(rootItemTplId, rootItemId, itemWithChildren);

        foreach (var pouch in pouches.OrderByDescending(p => p.ForRifleMags == wantsMagazinePouch))
        {
            if (TryStoreIn(pouch, itemWithChildren, rootItemTplId, width, height))
            {
                inventory.Items.AddRange(itemWithChildren);
                return true;
            }
        }

        return false;
    }

    private bool TryStoreIn(BotPouch pouch, List<Item> itemWithChildren, MongoId tpl, int width, int height)
    {
        var grids = pouch.Details.ContainerDbItem.Properties?.Grids?.ToList();
        if (grids == null || pouch.Details.ContainerFull)
        {
            return false;
        }

        for (var index = 0; index < grids.Count && index < pouch.Details.ContainerGridDetails.Count; index++)
        {
            var grid = grids[index];
            var map = pouch.Details.ContainerGridDetails[index];
            if (map.GridFull || !AllowedIn(grid, tpl) || IsBiggerThan(map.GridMap, width, height))
            {
                continue;
            }

            var slot = map.GridMap.FindSlotForItem(width, height);
            if (slot.Success != true)
            {
                if (width == 1 && height == 1)
                {
                    map.GridFull = true;
                }

                continue;
            }

            var rotated = slot.Rotation == true;
            var root = itemWithChildren[0];
            root.ParentId = pouch.Details.ContainerInventoryItem.Id;
            root.SlotId = grid.Name;
            root.Location = new ItemLocation
            {
                X = slot.X,
                Y = slot.Y,
                R = rotated ? ItemRotation.Vertical : ItemRotation.Horizontal,
            };

            Fill(map.GridMap, slot.X!.Value, slot.Y!.Value,
                rotated ? height : width, rotated ? width : height);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a section takes this kind of item at all. The game's own check reads the first
    /// section's filter and applies it to the whole container; a pouch's sections can differ.
    /// </summary>
    private bool AllowedIn(Grid grid, MongoId tpl)
    {
        var filter = grid.Properties?.Filters?.FirstOrDefault();
        if (filter == null)
        {
            return true;
        }

        var parent = templateTable.Items.TryGetValue(tpl, out var template) ? template.Parent : default;
        if (filter.ExcludedFilter?.Contains(parent) == true || filter.ExcludedFilter?.Contains(tpl) == true)
        {
            return false;
        }

        var allowed = filter.Filter;
        return allowed == null || allowed.Count == 0 ||
               (allowed.Count == 1 && allowed.Contains(BaseClasses.ITEM)) ||
               allowed.Contains(parent) || allowed.Contains(tpl);
    }

    private static bool IsBiggerThan(int[,] grid, int width, int height)
    {
        var rows = grid.GetLength(0);
        var columns = grid.GetLength(1);
        return !(width <= columns && height <= rows) && !(height <= columns && width <= rows);
    }

    private static void Fill(int[,] grid, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                grid[row, column] = 1;
            }
        }
    }
}
