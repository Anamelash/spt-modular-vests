using ModularVests.Server.Patches;
using ModularVests.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ModularVests.Server;

/// <summary>
/// The bot half: the rigs go into the equipment pools and the patches that fill them go up.
///
/// Late on purpose. The pools are read from every bot type as they stand, so every mod that
/// adds prototypes to bots has to have had its turn; APBS imports its own tables a little
/// later still (1 000 069), which is why <see cref="ApbsSyncPatch"/> has to be in place by now.
/// </summary>
[Injectable(TypePriority = 1_000_000)]
public class BotRegistration(
    ModConfigs configs,
    PatchTargets targets,
    BotPoolRegistrar pools,
    BotPouchCapacityPatch capacityPatch,
    BotInventoryDonePatch inventoryPatch,
    PmcBackpackLootPatch pmcLootPatch,
    ApbsSyncPatch apbsPatch,
    ISptLogger<BotRegistration> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var items = configs.Items;
        var bots = configs.Bots;
        if (items == null || bots == null)
        {
            return Task.CompletedTask; // already said why
        }

        if (!targets.Available)
        {
            return Task.CompletedTask; // already said why
        }

        logger.Info("[ModularVests] bots: " + pools.Register(items, bots));

        var patches = new List<SPTarkov.Reflection.Patching.AbstractPatch>
            { capacityPatch, inventoryPatch, pmcLootPatch };
        if (apbsPatch.IsApbsInstalled)
        {
            patches.Add(apbsPatch);
        }

        foreach (var patch in patches)
        {
            try
            {
                patch.Enable();
            }
            catch (Exception ex)
            {
                logger.Warning($"[ModularVests] {patch.GetType().Name} could not be applied: {ex.Message}. " +
                               "Bots may end up with empty rigs");
            }
        }

        return Task.CompletedTask;
    }
}
