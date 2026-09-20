using System.Reflection;
using ModularVests.Server.Config;
using ModularVests.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace ModularVests.Server;

/// <summary>
/// Entry point: registers the rigs and pouches while the server still accepts items, and puts
/// them into the assort of the trader that <see cref="TraderRegistration"/> added earlier.
/// The rigs' default presets go in here as well: PresetCallbacks and RagfairCallbacks, which
/// read them, both run later.
///
/// SaveCallbacks - 1000 is the last position before the item database closes (see
/// <see cref="ItemRegistrationWindow"/>) and well after WTT-ContentBackport, which adds
/// the 6B45 this line is cloned from at Preload + 2.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 1000)]
public class ItemRegistration(
    ModHelper modHelper,
    CustomItemService customItemService,
    ModularItemsRegistrar registrar,
    ModConfigs configs,
    ISptLogger<ItemRegistration> logger) : IOnLoad
{
    public const string ConfigFile = "items.jsonc";

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(modPath, "mod-files", ConfigFile);

        ItemsConfig config;
        try
        {
            config = ItemsConfig.Parse(await File.ReadAllTextAsync(configPath, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.Error($"[ModularVests] cannot read {configPath}: {ex.Message}. No items registered");
            return;
        }

        var errors = config.Validate();
        if (errors.Count > 0)
        {
            logger.Error($"[ModularVests] {ConfigFile} is invalid, no items registered:\n  " +
                         string.Join("\n  ", errors));
            return;
        }

        foreach (var warning in config.Warnings())
        {
            logger.Warning($"[ModularVests] {ConfigFile}: {warning}");
        }

        if (!ItemRegistrationWindow.IsOpen(customItemService))
        {
            logger.Error("[ModularVests] the server closed the item database before the mod could " +
                         "register its items. The mod needs an update for this server version");
            return;
        }

        configs.UseItems(config);
        var summary = registrar.Register(config);
        if (summary != null)
        {
            logger.Success($"[ModularVests] loaded: {summary}");
        }
    }
}
