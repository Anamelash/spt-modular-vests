using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularVests.Server.Config;

/// <summary>
/// The trader that sells the line-up, as shipped in <c>mod-files/trader/trader.jsonc</c>.
/// Only what is safe to tune lives here; the rest of the trader (no insurance, no repair,
/// unlocked from the start...) is fixed in <see cref="Services.TraderRegistrar"/>, so that
/// the file cannot describe a broken trader.
/// </summary>
public sealed class TraderConfigFile
{
    public const int LoyaltyLevelCount = ItemsConfig.MaxLoyaltyLevel;

    public static readonly string[] Currencies = ["RUB", "USD", "EUR"];

    /// <summary>
    /// The trader's id follows from it and is stored in profiles (TradersInfo): never rename
    /// a shipped key.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>Currency the trader pays in when buying back: RUB, USD or EUR.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "RUB";

    /// <summary>Seconds between assort refreshes.</summary>
    [JsonPropertyName("refreshSeconds")]
    public RefreshSeconds RefreshSeconds { get; set; } = new();

    /// <summary>Whether the trader's offers are listed on the flea market.</summary>
    [JsonPropertyName("sellOnFlea")]
    public bool SellOnFlea { get; set; } = true;

    /// <summary>Percent the trader keeps when buying an item back (buy_price_coef), every level.</summary>
    [JsonPropertyName("buyCoef")]
    public double BuyCoef { get; set; } = 40;

    [JsonPropertyName("loyaltyLevels")]
    public List<LoyaltyLevelConfig> LoyaltyLevels { get; set; } = [];

    /// <summary>By language; <c>en</c> is required and stands in for the languages not listed.</summary>
    [JsonPropertyName("locales")]
    public Dictionary<string, TraderLocale> Locales { get; set; } = [];

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TraderConfigFile Parse(string json) =>
        JsonSerializer.Deserialize<TraderConfigFile>(json, ReadOptions)
        ?? throw new JsonException("trader config is empty");

    /// <summary>Configuration errors that would produce a broken trader; empty when the file is usable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Key))
        {
            errors.Add("key is empty");
        }

        if (!Currencies.Contains(Currency))
        {
            errors.Add($"currency '{Currency}' is not one of {string.Join(", ", Currencies)}");
        }

        if (RefreshSeconds.Min < 1 || RefreshSeconds.Min > RefreshSeconds.Max)
        {
            errors.Add("refreshSeconds: min must be at least 1 and not above max");
        }

        if (BuyCoef is < 0 or > 100)
        {
            errors.Add("buyCoef must be 0..100");
        }

        if (LoyaltyLevels.Count != LoyaltyLevelCount)
        {
            errors.Add($"loyaltyLevels: exactly {LoyaltyLevelCount} levels are required");
        }

        foreach (var (level, index) in LoyaltyLevels.Select((l, i) => (l, i)))
        {
            if (level.MinLevel < 1 || level.MinSalesSum < 0)
            {
                errors.Add($"loyaltyLevels[{index}]: minLevel must be at least 1 and minSalesSum not negative");
            }
        }

        if (!Locales.TryGetValue("en", out var en))
        {
            errors.Add("locales: 'en' is required");
        }
        else if (string.IsNullOrWhiteSpace(en.Nickname))
        {
            errors.Add("locales.en: nickname is empty");
        }

        return errors;
    }

    /// <summary>The text for a language: its own, or English.</summary>
    public TraderLocale LocaleFor(string language) =>
        Locales.TryGetValue(language, out var own) ? own : Locales["en"];
}

public sealed class RefreshSeconds
{
    [JsonPropertyName("min")]
    public int Min { get; set; } = 3600;

    [JsonPropertyName("max")]
    public int Max { get; set; } = 3600;
}

public sealed class LoyaltyLevelConfig
{
    /// <summary>Player level.</summary>
    [JsonPropertyName("minLevel")]
    public int MinLevel { get; set; } = 1;

    /// <summary>Money spent at the trader, in its currency.</summary>
    [JsonPropertyName("minSalesSum")]
    public long MinSalesSum { get; set; }

    [JsonPropertyName("minStanding")]
    public double MinStanding { get; set; }
}

public sealed class TraderLocale
{
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}
