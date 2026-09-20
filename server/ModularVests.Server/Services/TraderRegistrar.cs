using ModularVests.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using Path = System.IO.Path;

namespace ModularVests.Server.Services;

/// <summary>
/// Puts the mod's trader into the database: base, empty assort, avatar route, refresh time,
/// flea listing and locales. The line-up is added to the assort later, by
/// <see cref="ModularItemsRegistrar"/>. Idempotent: a trader that is already there is left alone.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class TraderRegistrar(
    TradersTable tradersTable,
    LocaleTable localeTable,
    TraderConfig traderConfig,
    RagfairConfig ragfairConfig,
    ImageRouter imageRouter,
    ISptLogger<TraderRegistrar> logger)
{
    public const string AvatarRoute = "/files/trader/avatar/";

    public const string RoublesTpl = "5449016a4bdc2d6f028b456f";

    /// <summary>Id of the registered trader; null until <see cref="Register"/> succeeds.</summary>
    public MongoId? TraderId { get; private set; }

    public static string IdFor(TraderConfigFile config) =>
        DeterministicId.For(DeterministicId.TraderKey(config.Key));

    /// <param name="avatarPath">Avatar file; a missing one leaves the trader without a picture.</param>
    public void Register(TraderConfigFile config, string avatarPath)
    {
        var id = new MongoId(IdFor(config));

        if (tradersTable.ContainsKey(id))
        {
            logger.Debug($"[ModularVests] trader '{config.Key}' already present ({id})");
            TraderId = id;
            return;
        }

        var avatarName = id + Path.GetExtension(avatarPath);
        if (File.Exists(avatarPath))
        {
            // the route key carries no extension
            imageRouter.AddRoute(AvatarRoute + id, avatarPath);
        }
        else
        {
            logger.Warning($"[ModularVests] trader avatar not found: {avatarPath}");
        }

        traderConfig.UpdateTime.Add(new UpdateTime
        {
            Name = config.Key,
            TraderId = id,
            Seconds = new MinMax<int>(config.RefreshSeconds.Min, config.RefreshSeconds.Max),
        });
        ragfairConfig.Traders.TryAdd(id, config.SellOnFlea);

        tradersTable.TryAdd(id, new Trader
        {
            Base = BuildBase(config, id, AvatarRoute + avatarName),
            Assort = new TraderAssort
            {
                NextResupply = 0,
                Items = [],
                BarterScheme = [],
                LoyalLevelItems = [],
            },
            QuestAssort = new Dictionary<string, Dictionary<MongoId, MongoId>>
            {
                ["started"] = [],
                ["success"] = [],
                ["fail"] = [],
            },
            Dialogue = [],
        });

        foreach (var (language, locale) in localeTable.Global)
        {
            var text = config.LocaleFor(language);
            locale.AddTransformer(data =>
            {
                if (data == null)
                {
                    return data;
                }

                data[$"{id} FullName"] = text.FullName;
                data[$"{id} FirstName"] = text.FirstName;
                data[$"{id} Nickname"] = text.Nickname;
                data[$"{id} Location"] = text.Location;
                data[$"{id} Description"] = text.Description;
                return data;
            });
        }

        TraderId = id;
        logger.Debug($"[ModularVests] trader '{config.Key}' registered as {id}");
    }

    /// <summary>The line-up is all the trader buys back; called once the items are registered.</summary>
    public void SetBoughtItems(IEnumerable<MongoId> tpls)
    {
        if (TraderId == null || !tradersTable.TryGetValue(TraderId.Value, out var trader))
        {
            return;
        }

        trader.Base.ItemsBuy ??= new ItemBuyData { Category = [], IdList = [] };
        trader.Base.ItemsBuy.IdList.UnionWith(tpls);
    }

    /// <summary>
    /// Everything the file does not say is fixed here: a seller of his own line-up and nothing
    /// else - no insurance, no repair, no healing, no clothing, available from the start.
    /// </summary>
    internal static TraderBase BuildBase(TraderConfigFile config, MongoId id, string avatar)
    {
        var en = config.LocaleFor("en");
        return new TraderBase
        {
            Id = id,
            Name = en.FirstName,
            Surname = " ",
            Nickname = en.Nickname,
            Location = en.Location,
            Avatar = avatar,
            Currency = Enum.Parse<CurrencyType>(config.Currency),
            AvailableInRaid = false,
            UnlockedByDefault = true,
            CustomizationSeller = false,
            Medic = false,
            BuyerUp = true,
            IsAvailableInPVE = true,
            IsCanTransferItems = false,
            IsCanTransferItemsFromPve = false,
            BalanceRub = 5000000,
            BalanceDollar = 0,
            BalanceEuro = 0,
            Discount = 0,
            DiscountEnd = 0,
            GridHeight = 150,
            NextResupply = 0,
            ProhibitedItemsSellModifier = 0,
            // filled with the line-up by SetBoughtItems; no category: SimpleContainer would take in cases
            ItemsBuy = new ItemBuyData { Category = [], IdList = [] },
            ItemsBuyProhibited = new ItemBuyData { Category = [], IdList = [] },
            TransferableItems = new ItemBuyData { Category = [], IdList = [] },
            ProhibitedTransferableItems = new ItemBuyData { Category = [], IdList = [] },
            SellCategory = [],
            Insurance = new TraderInsurance
            {
                Availability = false,
                ExcludedCategory = [],
                MinPayment = 0,
                MinReturnHour = 0,
                MaxReturnHour = 0,
                MaxStorageTime = 0,
            },
            Repair = new TraderRepair
            {
                Availability = false,
                Currency = RoublesTpl,
                CurrencyCoefficient = 1,
                ExcludedCategory = [],
                ExcludedIdList = [],
                PriceRate = 0,
                Quality = 1,
            },
            LoyaltyLevels = config.LoyaltyLevels.Select(level => new TraderLoyaltyLevel
            {
                BuyPriceCoefficient = config.BuyCoef,
                ExchangePriceCoefficient = 0,
                HealPriceCoefficient = 0,
                InsurancePriceCoefficient = 0,
                RepairPriceCoefficient = 0,
                MinLevel = level.MinLevel,
                MinSalesSum = level.MinSalesSum,
                MinStanding = level.MinStanding,
            }).ToList(),
        };
    }
}
