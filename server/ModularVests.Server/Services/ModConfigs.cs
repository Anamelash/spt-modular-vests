using System.Reflection;
using ModularVests.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;

namespace ModularVests.Server.Services;

/// <summary>
/// The mod's own files, read once and shared: the line-up (put here by
/// <see cref="ItemRegistration"/>, which owns reading and validating it) and the bot and loot
/// numbers (read here, on first use).
///
/// A singleton because the Harmony patches need the same numbers as the load-time registrars,
/// long after startup; <c>[Injectable]</c> is not a singleton by default.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ModConfigs(ModHelper modHelper, ISptLogger<ModConfigs> logger)
{
    public const string ConfigFile = "bots.jsonc";

    private BotsConfig? _bots;
    private bool _botsRead;
    private BotRigOutfitter? _outfitter;

    /// <summary>The line-up, once <see cref="ItemRegistration"/> has read and validated it.</summary>
    public ItemsConfig? Items { get; private set; }

    /// <summary>Called by the item registration with the config it validated.</summary>
    public void UseItems(ItemsConfig items) => Items = items;

    /// <summary>
    /// The bot and loot numbers, or null when the file is missing, broken or switched off:
    /// then the bot and loot half stays out of the way and the rest of the mod works.
    /// </summary>
    public BotsConfig? Bots
    {
        get
        {
            if (_botsRead)
            {
                return _bots;
            }

            _botsRead = true;
            _bots = ReadBots();
            return _bots;
        }
    }

    /// <summary>The outfitter the bots and the loose loot share, or null when either file is out.</summary>
    public BotRigOutfitter? Outfitter =>
        Items != null && Bots != null ? _outfitter ??= new BotRigOutfitter(Items, Bots) : null;

    /// <summary>Template id of a rig of the line-up.</summary>
    public static MongoId VestTpl(string vestKey) =>
        new(DeterministicId.For(DeterministicId.VestKey(vestKey)));

    /// <summary>Template id of a pouch of the line-up, colour and all.</summary>
    public static MongoId PouchTpl(string pouchKey) =>
        new(DeterministicId.For(DeterministicId.PouchKey(pouchKey)));

    private BotsConfig? ReadBots()
    {
        var path = Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "mod-files", ConfigFile);

        BotsConfig config;
        try
        {
            config = BotsConfig.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            logger.Warning($"[ModularVests] cannot read {path}: {ex.Message}. Bots keep their " +
                           "vanilla rigs and the loot tables are left alone");
            return null;
        }

        var errors = config.Validate();
        if (errors.Count > 0)
        {
            logger.Warning($"[ModularVests] {ConfigFile} is invalid, bots and loot are left alone:\n  " +
                           string.Join("\n  ", errors));
            return null;
        }

        if (!config.Enabled)
        {
            logger.Info($"[ModularVests] {ConfigFile}: disabled, bots and loot are left alone");
            return null;
        }

        return config;
    }
}
