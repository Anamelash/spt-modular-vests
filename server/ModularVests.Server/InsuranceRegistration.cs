using ModularVests.Server.Patches;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;

namespace ModularVests.Server;

/// <summary>
/// Insurance of a modular rig covers its pouches. Nothing here depends on the bot half or its
/// config: an insured rig is the player's, whatever bots wear.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 800)]
public class InsuranceRegistration(
    PatchTargets targets,
    PreRaidPouchesPatch preRaidPatch,
    InsuredPouchesPatch insuredPatch,
    PouchesKeptPatch keptPatch,
    ISptLogger<InsuranceRegistration> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!targets.InsuranceAvailable)
        {
            return Task.CompletedTask; // already said why
        }

        foreach (AbstractPatch patch in new AbstractPatch[] { preRaidPatch, insuredPatch, keptPatch })
        {
            try
            {
                patch.Enable();
            }
            catch (Exception ex)
            {
                logger.Warning($"[ModularVests] {patch.GetType().Name} could not be applied: {ex.Message}. " +
                               "Insured rigs may come back without their pouches");
            }
        }

        return Task.CompletedTask;
    }
}
