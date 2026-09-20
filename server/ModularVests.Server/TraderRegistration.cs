using System.Reflection;
using ModularVests.Server.Config;
using ModularVests.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;

namespace ModularVests.Server;

/// <summary>
/// Entry point of the trader: registered early and empty, so that everything that walks the
/// trader table later (other mods, TraderCallbacks, which requires a refresh time for every
/// trader) already sees him. <see cref="ItemRegistration"/> fills his assort.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class TraderRegistration(
    ModHelper modHelper,
    TraderRegistrar registrar,
    ISptLogger<TraderRegistration> logger) : IOnLoad
{
    public const string ConfigFile = "trader.jsonc";

    public const string AvatarFile = "avatar.jpg";

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var traderPath = Path.Combine(modPath, "mod-files", "trader");
        var configPath = Path.Combine(traderPath, ConfigFile);

        TraderConfigFile config;
        try
        {
            config = TraderConfigFile.Parse(await File.ReadAllTextAsync(configPath, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.Error($"[ModularVests] cannot read {configPath}: {ex.Message}. No trader registered");
            return;
        }

        var errors = config.Validate();
        if (errors.Count > 0)
        {
            logger.Error($"[ModularVests] {ConfigFile} is invalid, no trader registered:\n  " +
                         string.Join("\n  ", errors));
            return;
        }

        registrar.Register(config, Path.Combine(traderPath, AvatarFile));
    }
}
