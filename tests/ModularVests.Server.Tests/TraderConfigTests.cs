using ModularVests.Server.Config;
using ModularVests.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace ModularVests.Server.Tests;

public class TraderConfigTests
{
    private static TraderConfigFile Shipped() => TraderConfigFile.Parse(File.ReadAllText(TestPaths.TraderConfig));

    [Fact]
    public void Shipped_config_is_valid()
    {
        var config = Shipped();
        Assert.Empty(config.Validate());
        Assert.Equal("anatoly", config.Key);
        Assert.Equal(4, config.LoyaltyLevels.Count);
        Assert.Equal(1, config.LoyaltyLevels[0].MinLevel);
        Assert.Contains("ru", config.Locales.Keys);
    }

    /// <summary>
    /// The trader's id is stored in profiles (TradersInfo). Never "fix" this test by updating
    /// the constant, and never rename the shipped key.
    /// </summary>
    [Fact]
    public void Trader_id_is_frozen()
    {
        Assert.Equal(FrozenTraderId, TraderRegistrar.IdFor(Shipped()));
        Assert.Equal(FrozenTraderId, DeterministicId.For("trader:anatoly"));
    }

    private const string FrozenTraderId = "cd195d736190531b0aea35a5";

    [Fact]
    public void A_language_without_a_locale_gets_english()
    {
        var config = Shipped();
        Assert.Equal("Anatoly", config.LocaleFor("fr").Nickname);
        Assert.Equal("Анатолий", config.LocaleFor("ru").Nickname);
    }

    [Fact]
    public void Validation_rejects_a_broken_trader()
    {
        var config = Shipped();
        config.Key = " ";
        config.Currency = "GP";
        config.RefreshSeconds = new RefreshSeconds { Min = 600, Max = 60 };
        config.BuyCoef = 140;
        config.LoyaltyLevels.RemoveAt(3);
        config.Locales.Remove("en");

        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("key is empty"));
        Assert.Contains(errors, e => e.Contains("currency 'GP'"));
        Assert.Contains(errors, e => e.Contains("refreshSeconds"));
        Assert.Contains(errors, e => e.Contains("buyCoef"));
        Assert.Contains(errors, e => e.Contains("exactly 4 levels"));
        Assert.Contains(errors, e => e.Contains("'en' is required"));
    }

    [Fact]
    public void Validation_rejects_a_bad_loyalty_level()
    {
        var config = Shipped();
        config.LoyaltyLevels[1].MinLevel = 0;
        Assert.Contains(config.Validate(), e => e.Contains("loyaltyLevels[1]"));
    }

    /// <summary>A seller of his own line-up and nothing else, whatever the file says.</summary>
    [Fact]
    public void Base_is_built_from_the_config()
    {
        var config = Shipped();
        var id = new MongoId(TraderRegistrar.IdFor(config));
        var trader = TraderRegistrar.BuildBase(config, id, "/files/trader/avatar/x.jpg");

        Assert.Equal(id, trader.Id);
        Assert.Equal("Anatoly", trader.Nickname);
        Assert.Equal(CurrencyType.RUB, trader.Currency);
        Assert.True(trader.UnlockedByDefault);
        Assert.False(trader.Insurance!.Availability);
        Assert.False(trader.Repair!.Availability);
        Assert.False(trader.Medic);
        Assert.False(trader.CustomizationSeller);
        Assert.Empty(trader.ItemsBuy!.Category);
        Assert.Empty(trader.ItemsBuy.IdList);
        Assert.Equal(4, trader.LoyaltyLevels!.Count);
        Assert.All(trader.LoyaltyLevels, level => Assert.Equal(config.BuyCoef, level.BuyPriceCoefficient));
        Assert.Equal([1, 15, 26, 36], trader.LoyaltyLevels.Select(l => l.MinLevel!.Value));
    }
}
