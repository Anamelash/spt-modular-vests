using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularVests.Server.Config;

/// <summary>
/// <c>mod-files/bots.jsonc</c>: every number the pouch outfitter rolls and every weight the
/// loot tables get. Broken or missing, the bot and loot half stays off and the rest of the
/// mod works - see <see cref="Services.BotRigOutfitter"/> and <see cref="Services.LootRegistrar"/>.
/// </summary>
public sealed class BotsConfig
{
    /// <summary>Off: bots keep their vanilla rigs and nothing is added to loot.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>How often a rig turns up among rigs, relative to its prototype among armor vests.</summary>
    [JsonPropertyName("weightMultiplier")]
    public double WeightMultiplier { get; set; } = 1.0;

    [JsonPropertyName("offColourChance")]
    public OffColourChance OffColourChance { get; set; } = new();

    /// <summary>d100 to a number of rifle magazine slots on the chest.</summary>
    [JsonPropertyName("rifleMagSlots")]
    public List<MagSlotRoll> RifleMagSlots { get; set; } = [];

    /// <summary>Chance (%) that the cells left over on the chest stay empty; one roll per chest.</summary>
    [JsonPropertyName("chestLeftoverEmptyChance")]
    public int ChestLeftoverEmptyChance { get; set; } = 50;

    [JsonPropertyName("cummerbund")]
    public CummerbundRoll Cummerbund { get; set; } = new();

    [JsonPropertyName("loot")]
    public LootConfig Loot { get; set; } = new();

    /// <summary>The number of magazine slots a d100 roll (1..100) asks for.</summary>
    public int MagSlotsFor(int roll)
    {
        foreach (var row in RifleMagSlots)
        {
            if (roll <= row.UpTo)
            {
                return row.Slots;
            }
        }

        return RifleMagSlots.Count > 0 ? RifleMagSlots[^1].Slots : 0;
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static BotsConfig Parse(string json) =>
        JsonSerializer.Deserialize<BotsConfig>(json, ReadOptions)
        ?? throw new JsonException("bots config is empty");

    /// <summary>Configuration errors that would make the outfitter roll nonsense; empty when usable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (WeightMultiplier <= 0)
        {
            errors.Add("weightMultiplier must be positive");
        }

        if (OffColourChance.Matched is < 0 or > 100 || OffColourChance.Random is < 0 or > 100)
        {
            errors.Add("offColourChance: both chances must be 0..100");
        }

        if (RifleMagSlots.Count == 0)
        {
            errors.Add("rifleMagSlots: no rows - nothing says how many magazines a chest carries");
        }

        var previous = 0;
        foreach (var row in RifleMagSlots)
        {
            if (row.UpTo <= previous || row.UpTo > 100)
            {
                errors.Add($"rifleMagSlots: upTo {row.UpTo} must rise towards 100");
            }

            if (row.Slots is < 0 or > ChestColumns * 2 || row.Slots % 2 != 0)
            {
                errors.Add($"rifleMagSlots: slots {row.Slots} must be an even number up to {ChestColumns * 2}");
            }

            previous = row.UpTo;
        }

        if (RifleMagSlots.Count > 0 && RifleMagSlots[^1].UpTo != 100)
        {
            errors.Add("rifleMagSlots: the last row must reach 100");
        }

        if (ChestLeftoverEmptyChance is < 0 or > 100)
        {
            errors.Add("chestLeftoverEmptyChance must be 0..100");
        }

        if (Cummerbund.EmptyUpTo < 0 || Cummerbund.PartialUpTo < Cummerbund.EmptyUpTo ||
            Cummerbund.PartialUpTo > 100)
        {
            errors.Add("cummerbund: emptyUpTo and partialUpTo must rise towards 100");
        }

        if (Cummerbund.PartialArea is < 1 or > ClusterGrid.CellsPerCluster)
        {
            errors.Add($"cummerbund: partialArea must be 1..{ClusterGrid.CellsPerCluster}");
        }

        errors.AddRange(Loot.Validate());
        return errors;
    }

    /// <summary>Columns of cells a rig's chest offers: two clusters of two columns.</summary>
    public const int ChestColumns = 4;
}

public sealed class OffColourChance
{
    /// <summary>Chance (%) a pouch strays from a rig that has a colour to match.</summary>
    [JsonPropertyName("matched")]
    public int Matched { get; set; }

    /// <summary>Chance (%) a pouch strays when the bot's base colour was rolled at random.</summary>
    [JsonPropertyName("random")]
    public int Random { get; set; }
}

public sealed class MagSlotRoll
{
    [JsonPropertyName("upTo")]
    public int UpTo { get; set; }

    [JsonPropertyName("slots")]
    public int Slots { get; set; }
}

public sealed class CummerbundRoll
{
    /// <summary>Rolls up to here leave the cluster empty.</summary>
    [JsonPropertyName("emptyUpTo")]
    public int EmptyUpTo { get; set; }

    /// <summary>Rolls up to here cover exactly <see cref="PartialArea"/> cells; above, the cluster fills up.</summary>
    [JsonPropertyName("partialUpTo")]
    public int PartialUpTo { get; set; }

    [JsonPropertyName("partialArea")]
    public int PartialArea { get; set; } = 2;
}

public sealed class LootConfig
{
    [JsonPropertyName("staticRigs")]
    public StaticRigs StaticRigs { get; set; } = new();

    [JsonPropertyName("looseRigs")]
    public LooseRigs LooseRigs { get; set; } = new();

    [JsonPropertyName("staticPouches")]
    public List<StaticPouchEntry> StaticPouches { get; set; } = [];

    [JsonPropertyName("botBackpacks")]
    public LikeWeight BotBackpacks { get; set; } = new();

    [JsonPropertyName("pmcBackpack")]
    public LikeWeight PmcBackpack { get; set; } = new();

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (StaticRigs.WeightOfPrototype < 0)
        {
            errors.Add("loot.staticRigs.weightOfPrototype cannot be negative");
        }

        if (LooseRigs.WeightOfPrototype < 0)
        {
            errors.Add("loot.looseRigs.weightOfPrototype cannot be negative");
        }

        if (LooseRigs.LoadoutsPerRig < 0)
        {
            errors.Add("loot.looseRigs.loadoutsPerRig cannot be negative");
        }

        foreach (var entry in StaticPouches)
        {
            if (string.IsNullOrWhiteSpace(entry.Container) || string.IsNullOrWhiteSpace(entry.Like))
            {
                errors.Add("loot.staticPouches: an entry has no container or no reference item");
            }

            if (entry.Weight < 0)
            {
                errors.Add($"loot.staticPouches: weight of container '{entry.Container}' cannot be negative");
            }
        }

        return errors;
    }
}

public sealed class StaticRigs
{
    [JsonPropertyName("weightOfPrototype")]
    public double WeightOfPrototype { get; set; }
}

public sealed class LooseRigs
{
    [JsonPropertyName("weightOfPrototype")]
    public double WeightOfPrototype { get; set; }

    /// <summary>How many different pouch loadouts of one rig a spawn point offers.</summary>
    [JsonPropertyName("loadoutsPerRig")]
    public int LoadoutsPerRig { get; set; }
}

public sealed class StaticPouchEntry
{
    /// <summary>Template of the container the pouches go into.</summary>
    [JsonPropertyName("container")]
    public string Container { get; set; } = "";

    /// <summary>Item already in that container whose weight the pouches are measured against.</summary>
    [JsonPropertyName("like")]
    public string Like { get; set; } = "";

    [JsonPropertyName("weight")]
    public double Weight { get; set; }
}

public sealed class LikeWeight
{
    /// <summary>Item already in the pool whose weight the pouches are measured against.</summary>
    [JsonPropertyName("like")]
    public string Like { get; set; } = "";

    [JsonPropertyName("weight")]
    public double Weight { get; set; }
}
