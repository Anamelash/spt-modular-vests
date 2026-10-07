using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace ModularVests.Server.Patches;

/// <summary>
/// The pouches a PMC took into the raid, read before the raid's results replace the inventory:
/// a rig dropped on the map is no longer in it, and insurance still owes its pouches.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PreRaidPouchesPatch(InsuredPouches pouches) : AbstractPatch
{
    private static InsuredPouches _pouches = null!;

    protected override MethodBase? GetTargetMethod()
    {
        _pouches = pouches;
        return PatchTargets.HandlePostRaidPmc;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, SptProfile fullServerProfile)
    {
        _pouches.Remember(sessionId, fullServerProfile.CharacterData?.PmcData?.Inventory?.Items);
    }
}
