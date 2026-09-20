using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModularVests.Server.Config;

/// <summary>
/// The line-up as shipped in <c>mod-files/items.jsonc</c>: which rigs exist, which pouches
/// exist, what goes where and what it costs. Ids are not written here — they follow from
/// the keys (see <see cref="Services.DeterministicId"/>).
/// </summary>
public sealed class ItemsConfig
{
    [JsonPropertyName("vests")]
    public List<VestConfig> Vests { get; set; } = [];

    [JsonPropertyName("pouches")]
    public List<PouchConfig> Pouches { get; set; } = [];

    /// <summary>
    /// Name of a pouch cell (language -> text), shown over an empty cell in the inspect window;
    /// "en" stands in for a language without its own. Empty: the cells show their slot id.
    /// </summary>
    [JsonPropertyName("pouchSlotName")]
    public Dictionary<string, string> PouchSlotName { get; set; } = [];

    /// <summary>The cell name in a language, falling back to English; null when there is none.</summary>
    public string? PouchSlotNameFor(string language) =>
        PouchSlotName.TryGetValue(language, out var text) && !string.IsNullOrWhiteSpace(text) ? text
        : PouchSlotName.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en) ? en
        : null;

    /// <summary>
    /// Colour name per variant key, per language (<c>coyote -> en -> "Coyote"</c>). Every pouch
    /// offers the same colours, so they are written once here instead of once per pouch; a
    /// variant that sets its own <c>color</c> keeps it.
    /// </summary>
    [JsonPropertyName("colors")]
    public Dictionary<string, Dictionary<string, string>> Colors { get; set; } = [];

    /// <summary>
    /// Description (language -> text) every rig gets unless it writes its own. Rigs differ in
    /// model and protection, which the name and the stats already say; the description says the
    /// one thing they all share - where the storage comes from.
    /// </summary>
    [JsonPropertyName("vestDescription")]
    public Dictionary<string, string> VestDescription { get; set; } = [];

    /// <summary>Description (language -> text) every pouch gets unless it writes its own.</summary>
    [JsonPropertyName("pouchDescription")]
    public Dictionary<string, string> PouchDescription { get; set; } = [];

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ItemsConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<ItemsConfig>(json, ReadOptions)
            ?? throw new JsonException("items config is empty");
        config.Normalize();
        return config;
    }

    public string Serialize() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>
    /// Copies the shared blocks into the items that left the field out, so that everything
    /// downstream sees one shape: a variant without a <c>color</c> takes it from
    /// <see cref="Colors"/>, an item without a description takes the one for its kind (its own
    /// language, else English). Run on every parse; running it twice changes nothing.
    /// </summary>
    public void Normalize()
    {
        foreach (var variant in Pouches.SelectMany(p => p.Variants))
        {
            if (variant.Colors.Count == 0 && Colors.TryGetValue(variant.Key, out var shared))
            {
                variant.Colors = new Dictionary<string, string>(shared);
            }
        }

        foreach (var variant in Vests.SelectMany(v => v.Variants))
        {
            if (variant.Colors.Count == 0 && Colors.TryGetValue(variant.Key, out var shared))
            {
                variant.Colors = new Dictionary<string, string>(shared);
            }
        }

        FillDescriptions(Vests.Select(v => v.Locales), VestDescription);
        FillDescriptions(Pouches.Select(p => p.Locales), PouchDescription);
    }

    private static void FillDescriptions(
        IEnumerable<Dictionary<string, LocaleText>> items,
        Dictionary<string, string> shared)
    {
        if (shared.Count == 0)
        {
            return;
        }

        shared.TryGetValue("en", out var english);
        foreach (var (language, text) in items.SelectMany(locales => locales))
        {
            if (!string.IsNullOrWhiteSpace(text.Description))
            {
                continue;
            }

            var value = shared.TryGetValue(language, out var own) ? own : english;
            if (!string.IsNullOrWhiteSpace(value))
            {
                text.Description = value;
            }
        }
    }

    /// <summary>Configuration errors that would produce a broken item; empty when the file is usable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        var pouchKeys = new HashSet<string>();
        if (PouchSlotName.Count > 0 && PouchSlotNameFor("en") == null)
        {
            errors.Add("pouchSlotName: no \"en\" text, which stands in for the other languages");
        }


        foreach (var variant in Pouches.SelectMany(p => p.Variants.Select(v => (p, v))))
        {
            if (string.IsNullOrWhiteSpace(variant.v.Key))
            {
                errors.Add($"pouch '{variant.p.Key}': a variant has no key");
            }
            else if (variant.v.Colors.Count == 0)
            {
                errors.Add($"pouch '{variant.p.Key}': variant '{variant.v.Key}' has no colour text " +
                           "and \"colors\" has no entry for it");
            }
        }

        foreach (var p in AllPouches())
        {
            if (string.IsNullOrWhiteSpace(p.Key) || !pouchKeys.Add(p.Key))
            {
                errors.Add($"pouch key '{p.Key}' is empty or duplicated");
            }

            if (p.Grids.Count == 0)
            {
                errors.Add($"pouch '{p.Key}': no grids");
            }

            var gridNames = new HashSet<string>();
            foreach (var grid in p.Grids)
            {
                if (string.IsNullOrWhiteSpace(grid.Name) || !gridNames.Add(grid.Name))
                {
                    errors.Add($"pouch '{p.Key}': grid name '{grid.Name}' is empty or duplicated");
                }

                if (grid.Width < 1 || grid.Height < 1)
                {
                    errors.Add($"pouch '{p.Key}': grid '{grid.Name}' must be at least 1x1");
                }
            }

            if (p.Price <= 0)
            {
                errors.Add($"pouch '{p.Key}': price must be positive");
            }

            if (!IsLoyaltyLevel(p.LoyaltyLevel))
            {
                errors.Add($"pouch '{p.Key}': loyaltyLevel must be {MinLoyaltyLevel}..{MaxLoyaltyLevel}");
            }

            if (!Footprint.TryParse(p.Footprint, out _))
            {
                errors.Add($"pouch '{p.Key}': footprint '{p.Footprint}' is not one of " +
                           string.Join(", ", Footprint.All));
            }

            if (p.BotUse.Length > 0 && p.BotUse != RifleMagUse)
            {
                errors.Add($"pouch '{p.Key}': botUse '{p.BotUse}' is not '{RifleMagUse}'");
            }

            if (p.IsRifleMagPouch ? p.MagSlots is < 1 or > 2 : p.MagSlots != 0)
            {
                errors.Add($"pouch '{p.Key}': magSlots {p.MagSlots} - a '{RifleMagUse}' pouch holds " +
                           "1 or 2 magazines, any other pouch holds none");
            }
        }

        var colours = Colours();

        foreach (var variant in Vests.SelectMany(v => v.Variants.Select(variant => (v, variant))))
        {
            if (string.IsNullOrWhiteSpace(variant.variant.Key))
            {
                errors.Add($"vest '{variant.v.Key}': a variant has no key");
            }
            else if (variant.variant.Colors.Count == 0)
            {
                errors.Add($"vest '{variant.v.Key}': variant '{variant.variant.Key}' has no colour text " +
                           "and \"colors\" has no entry for it");
            }
            else if (!colours.Contains(variant.variant.Key))
            {
                errors.Add($"vest '{variant.v.Key}': variant '{variant.variant.Key}' is not one of the " +
                           "line-up's colours - " + string.Join(", ", colours));
            }
        }

        var vestKeys = new HashSet<string>();
        foreach (var v in AllVests())
        {
            if (string.IsNullOrWhiteSpace(v.Key) || !vestKeys.Add(v.Key))
            {
                errors.Add($"vest key '{v.Key}' is empty or duplicated");
            }

            if (v.Price <= 0)
            {
                errors.Add($"vest '{v.Key}': price must be positive");
            }

            if (!IsLoyaltyLevel(v.LoyaltyLevel))
            {
                errors.Add($"vest '{v.Key}': loyaltyLevel must be {MinLoyaltyLevel}..{MaxLoyaltyLevel}");
            }

            foreach (var reserved in VestConfig.ReservedOverrides.Where(v.Overrides.ContainsKey))
            {
                errors.Add($"vest '{v.Key}': '{reserved}' is built by the mod and cannot be overridden");
            }

            if (v.Clusters < 1)
            {
                errors.Add($"vest '{v.Key}': clusters must be at least 1");
            }

            if (v.PouchColor.Length > 0 && !colours.Contains(v.PouchColor))
            {
                errors.Add($"vest '{v.Key}': pouchColor '{v.PouchColor}' is not one of " +
                           string.Join(", ", colours));
            }
        }

        foreach (var v in AllVests().Where(v => v.TierFrom.Length > 0))
        {
            if (v.TierFrom == v.Key || !vestKeys.Contains(v.TierFrom))
            {
                errors.Add($"vest '{v.Key}': tierFrom '{v.TierFrom}' is not another rig of the line-up");
            }
        }

        return errors;
    }

    /// <summary>The only <c>botUse</c> a pouch can carry: a rifle magazine pouch.</summary>
    public const string RifleMagUse = "rifleMag";

    /// <summary>
    /// Every colour key the line-up uses, in the order the first pouch lists them. A rig's
    /// <c>pouchColor</c> and a bot's random colour both come from here.
    /// </summary>
    public List<string> Colours() =>
        Pouches.SelectMany(p => p.Variants.Select(v => v.Key)).Distinct().ToList();

    /// <summary>Trader loyalty levels an item can be sold at.</summary>
    public const int MinLoyaltyLevel = 1;

    public const int MaxLoyaltyLevel = 4;

    private static bool IsLoyaltyLevel(int level) => level is >= MinLoyaltyLevel and <= MaxLoyaltyLevel;

    /// <summary>Things that work but are probably not what was meant.</summary>
    public List<string> Warnings()
    {
        var warnings = new List<string>();
        foreach (var v in AllVests().Where(v => v.Clusters > 0 && v.Clusters % 2 == 1))
        {
            warnings.Add($"vest '{v.Key}': {v.Clusters} clusters - cluster {v.Clusters} has no mirrored pair");
        }

        return warnings;
    }

    /// <summary>The pouches a cell in this position accepts: every one whose footprint fits from it.</summary>
    public List<PouchConfig> PouchesAt(int position) =>
        AllPouches().Where(p => Footprint.TryParse(p.Footprint, out var f) && ClusterGrid.Fits(position, f)).ToList();

    /// <summary>Every rig item the line-up makes: colour variants expanded.</summary>
    public List<VestConfig> AllVests() => Vests.SelectMany(v => v.Expand()).ToList();

    /// <summary>Every pouch item the line-up makes: colour variants expanded.</summary>
    public List<PouchConfig> AllPouches() => Pouches.SelectMany(p => p.Expand()).ToList();
}

public sealed class LocaleText
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public sealed class VestConfig
{
    /// <summary>Shared with the client (PouchSlots.Prefix): the only thing that marks a pouch slot.</summary>
    public const string PouchSlotPrefix = ClusterGrid.SlotPrefix;

    /// <summary>Template properties the registrar builds itself.</summary>
    public static readonly string[] ReservedOverrides = ["Slots", "Grids", "Name", "ShortName", "Description"];

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>
    /// Recolour kit the rig's bundles are built from (<c>assets/vests/&lt;model&gt;</c>): the three
    /// IOTV kits are three rigs off one carrier, so they share one. Empty: the same as the key.
    /// Read by <c>build/build-vest-bundles.ps1</c>; the server only checks it is a kit that exists.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>Template the rig is cloned from; its plate slots and model carry over.</summary>
    [JsonPropertyName("cloneTpl")]
    public string CloneTpl { get; set; } = "";

    /// <summary>
    /// The donor comes from a mod that may not be installed (a colour of another mod): without it
    /// this rig alone is left out. A missing donor of a rig that is not optional stops the whole
    /// registration.
    /// </summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    /// <summary>Class node of the clone — Vest, so the item goes to the TacticalVest slot.</summary>
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = "5448e5284bdc2dcb718b4567";

    /// <summary>
    /// Bundle the rig's model comes from (<c>modularvests/vests/otv_black.bundle</c>), built by
    /// <c>build/build-vest-bundles.ps1</c> from the donor's own bundle and a recoloured albedo.
    /// Empty: the donor's model is used as it is.
    /// </summary>
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    /// <summary>
    /// Asset name inside that bundle. A recoloured clone keeps the donor's prefab, so this is the
    /// donor's own rcid; empty takes the asset named like the bundle, which a clone is not.
    /// </summary>
    [JsonPropertyName("rcid")]
    public string Rcid { get; set; } = "";

    /// <summary>Internal template name (_name).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handbookParent")]
    public string HandbookParent { get; set; } = "5b5f6f8786f77447ed563642";

    [JsonPropertyName("price")]
    public double Price { get; set; }

    /// <summary>Trader loyalty level the rig is sold from (1-4).</summary>
    [JsonPropertyName("loyaltyLevel")]
    public int LoyaltyLevel { get; set; } = ItemsConfig.MinLoyaltyLevel;

    [JsonPropertyName("locales")]
    public Dictionary<string, LocaleText> Locales { get; set; } = [];

    /// <summary>
    /// Pouch clusters of 2x2 cells (see <see cref="ClusterGrid"/>): the registrar builds
    /// <c>clusters * 4</c> slots, mod_pouch_1 onwards. Clusters are only ever added - a slot
    /// that goes away orphans the pouches attached to it in existing profiles.
    /// </summary>
    [JsonPropertyName("clusters")]
    public int Clusters { get; set; }

    /// <summary>
    /// Colour the pouches a bot gets on this rig are matched to (a variant key). Empty: nothing
    /// on the carrier to match (a blue UNTAR), so the bot's base colour is rolled at random.
    /// </summary>
    [JsonPropertyName("pouchColor")]
    public string PouchColor { get; set; } = "";

    /// <summary>
    /// Key of another rig whose donor decides this one's tier, for a donor no bot ever wears
    /// (the Couturier colours: untar_dbdu takes untar's). Empty: this rig's own donor.
    /// </summary>
    [JsonPropertyName("tierFrom")]
    public string TierFrom { get; set; } = "";

    /// <summary>
    /// Template properties applied over the clone, by their database names
    /// (RigLayoutName, BlocksArmorVest, Width, Weight...). Slots and Grids are built by the
    /// mod and must not be set here.
    /// </summary>
    [JsonPropertyName("overrides")]
    public JsonObject Overrides { get; set; } = [];

    /// <summary>
    /// Colour variants: each becomes a rig of its own, off the same donor and the same layout,
    /// wearing a recoloured copy of the donor's bundle. <c>{variant}</c> in name, prefab and rcid
    /// is replaced by the variant key, <c>{color}</c> in the locales by the colour name from
    /// <see cref="ItemsConfig.Colors"/>. A variant that took over a key the mod already shipped
    /// names it in <see cref="VestVariant.ItemKey"/>; the others are keyed
    /// <c>&lt;key&gt;_&lt;variant key&gt;</c>. Empty: the entry is a rig as it is.
    /// </summary>
    [JsonPropertyName("variants")]
    public List<VestVariant> Variants { get; set; } = [];

    /// <summary>
    /// On a rig <see cref="Expand"/> produced: the carrier entry it came from and the colour it
    /// wears. The key alone does not say - a rig that took over a shipped key keeps that key.
    /// </summary>
    [JsonIgnore]
    public string Carrier { get; set; } = "";

    [JsonIgnore]
    public string Color { get; set; } = "";

    /// <summary>The rigs this entry stands for: itself, or one per colour variant.</summary>
    public IEnumerable<VestConfig> Expand()
    {
        if (Variants.Count == 0)
        {
            Carrier = Key;
            Model = Model.Length > 0 ? Model : Key;
            yield return this;
            yield break;
        }

        foreach (var variant in Variants)
        {
            string Fill(string text) => text.Replace("{variant}", variant.Key);

            yield return new VestConfig
            {
                Key = variant.ItemKey.Length > 0 ? variant.ItemKey : $"{Key}_{variant.Key}",
                Carrier = Key,
                Color = variant.Key,
                Model = Model.Length > 0 ? Model : Key,
                CloneTpl = CloneTpl,
                Optional = Optional,
                Parent = Parent,
                Prefab = Fill(Prefab),
                Rcid = Fill(Rcid),
                Name = Fill(Name),
                HandbookParent = HandbookParent,
                Price = Price,
                LoyaltyLevel = LoyaltyLevel,
                Clusters = Clusters,
                // the pouches a bot gets are matched to the rig it is wearing
                PouchColor = PouchColor.Length > 0 ? PouchColor : variant.Key,
                TierFrom = TierFrom,
                Overrides = Overrides,
                Locales = Locales.ToDictionary(kv => kv.Key, kv =>
                {
                    var color = variant.Colors.TryGetValue(kv.Key, out var own) ? own
                        : variant.Colors.TryGetValue("en", out var english) ? english : variant.Key;
                    return new LocaleText
                    {
                        Name = kv.Value.Name.Replace("{color}", color),
                        ShortName = kv.Value.ShortName.Replace("{color}", color),
                        Description = kv.Value.Description.Replace("{color}", color),
                    };
                }),
            };
        }
    }
}

public sealed class VestVariant
{
    /// <summary>Colour key; part of the rig key unless <see cref="ItemKey"/> takes over.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>
    /// Key of a rig the mod already shipped, which this colour takes the place of: the item keeps
    /// its id, its cells and its preset, and only its look changes. Profiles hold these keys, so
    /// one is never renamed or dropped (LegacyVestIdsTests).
    /// </summary>
    [JsonPropertyName("itemKey")]
    public string ItemKey { get; set; } = "";

    /// <summary>Colour name per locale, put where the locales say <c>{color}</c>.</summary>
    [JsonPropertyName("color")]
    public Dictionary<string, string> Colors { get; set; } = [];
}

public sealed class PouchConfig
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("cloneTpl")]
    public string CloneTpl { get; set; } = "5d235bb686f77443f4331278";

    [JsonPropertyName("parent")]
    public string Parent { get; set; } = "5795f317245977243854e041";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Model bundle (a vanilla one — the mod ships no bundles yet); empty keeps the donor's.</summary>
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    /// <summary>
    /// Cells the pouch takes on a rig, "WxH" (see <see cref="ClusterGrid"/>): decides which
    /// cells accept it. Unrelated to its size in the inventory (width/height).
    /// </summary>
    [JsonPropertyName("footprint")]
    public string Footprint { get; set; } = "";

    /// <summary>
    /// What a bot puts in this pouch first: <see cref="ItemsConfig.RifleMagUse"/> for a rifle
    /// magazine pouch, empty for anything else (see <see cref="Services.BotRigOutfitter"/>).
    /// </summary>
    [JsonPropertyName("botUse")]
    public string BotUse { get; set; } = "";

    /// <summary>Rifle magazines the pouch holds (1 or 2); 0 when it is not a magazine pouch.</summary>
    [JsonPropertyName("magSlots")]
    public int MagSlots { get; set; }

    /// <summary>Whether a bot treats this pouch as a rifle magazine pouch.</summary>
    [JsonIgnore]
    public bool IsRifleMagPouch => BotUse == ItemsConfig.RifleMagUse;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1;

    /// <summary>
    /// Asset name inside the bundle (Prefab.rcid); empty takes the asset named like the bundle.
    /// </summary>
    [JsonPropertyName("rcid")]
    public string Rcid { get; set; } = "";

    /// <summary>
    /// The pouch's sections. The names end up in profiles (an item in a pouch records the grid
    /// it lies in): never rename one that shipped.
    /// </summary>
    [JsonPropertyName("grids")]
    public List<GridConfig> Grids { get; set; } = [];

    /// <summary>
    /// Colour variants: each becomes a pouch of its own, keyed <c>&lt;key&gt;_&lt;variant key&gt;</c>.
    /// <c>{variant}</c> in name, prefab and rcid is replaced by the variant key, <c>{color}</c>
    /// in the locales by the variant's colour name, which comes from
    /// <see cref="ItemsConfig.Colors"/> unless the variant writes its own. Empty: the entry is
    /// a pouch as it is.
    /// </summary>
    [JsonPropertyName("variants")]
    public List<PouchVariant> Variants { get; set; } = [];

    /// <summary>Applies to every grid of the pouch.</summary>
    [JsonPropertyName("gridFilter")]
    public List<string> GridFilter { get; set; } = ["54009119af1c881c07000029"];

    [JsonPropertyName("gridExcludedFilter")]
    public List<string> GridExcludedFilter { get; set; } = [];

    [JsonPropertyName("weight")]
    public double Weight { get; set; }

    [JsonPropertyName("handbookParent")]
    public string HandbookParent { get; set; } = "5b5f6fa186f77409407a7eb7";

    [JsonPropertyName("price")]
    public double Price { get; set; }

    /// <summary>Trader loyalty level the pouch is sold from (1-4); colour variants share it.</summary>
    [JsonPropertyName("loyaltyLevel")]
    public int LoyaltyLevel { get; set; } = ItemsConfig.MinLoyaltyLevel;

    [JsonPropertyName("locales")]
    public Dictionary<string, LocaleText> Locales { get; set; } = [];

    /// <summary>The pouches this entry stands for: itself, or one per colour variant.</summary>
    public IEnumerable<PouchConfig> Expand()
    {
        if (Variants.Count == 0)
        {
            yield return this;
            yield break;
        }

        foreach (var variant in Variants)
        {
            string Fill(string text) => text.Replace("{variant}", variant.Key);

            yield return new PouchConfig
            {
                Key = $"{Key}_{variant.Key}",
                CloneTpl = CloneTpl,
                Parent = Parent,
                Name = Fill(Name),
                Prefab = Fill(Prefab),
                Rcid = Fill(Rcid),
                Footprint = Footprint,
                BotUse = BotUse,
                MagSlots = MagSlots,
                Width = Width,
                Height = Height,
                Grids = Grids,
                GridFilter = GridFilter,
                GridExcludedFilter = GridExcludedFilter,
                Weight = Weight,
                HandbookParent = HandbookParent,
                Price = Price,
                LoyaltyLevel = LoyaltyLevel,
                Locales = Locales.ToDictionary(kv => kv.Key, kv =>
                {
                    var color = variant.Colors.TryGetValue(kv.Key, out var c) ? c
                        : variant.Colors.TryGetValue("en", out var en) ? en : variant.Key;
                    return new LocaleText
                    {
                        Name = kv.Value.Name.Replace("{color}", color),
                        ShortName = kv.Value.ShortName.Replace("{color}", color),
                        Description = kv.Value.Description.Replace("{color}", color),
                    };
                }),
            };
        }
    }
}

public sealed class GridConfig
{
    /// <summary>Grid name as profiles store it (the item's slotId inside the pouch).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public sealed class PouchVariant
{
    /// <summary>Part of the pouch key: never rename one that shipped.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>Colour name per locale, put where the locales say <c>{color}</c>.</summary>
    [JsonPropertyName("color")]
    public Dictionary<string, string> Colors { get; set; } = [];
}
