using ModularVests.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ModularVests.Server;

/// <summary>
/// Puts the line-up into the loot tables, right after <see cref="ItemRegistration"/> has made
/// the items: the transformers need the template ids, and the location tables are read much
/// later (a raid), so this is only data.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 900)]
public class LootRegistration(
    ModConfigs configs,
    LootRegistrar registrar,
    ISptLogger<LootRegistration> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var items = configs.Items;
        var bots = configs.Bots;
        if (items == null || bots == null)
        {
            return Task.CompletedTask; // already said why
        }

        logger.Info("[ModularVests] loot: " + registrar.Register(items, bots));
        return Task.CompletedTask;
    }
}
