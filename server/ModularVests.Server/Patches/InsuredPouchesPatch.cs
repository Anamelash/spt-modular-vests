using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Services;

namespace ModularVests.Server.Patches;

/// <summary>
/// A lost insured rig takes its pouches home with it. The client reports only the insured items
/// it lost, so a pouch hung on after the rig was insured would not come back; this adds every
/// pouch of every lost rig to the rig's package.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class InsuredPouchesPatch(InsuredPouches pouches) : AbstractPatch
{
    private static InsuredPouches _pouches = null!;

    protected override MethodBase? GetTargetMethod()
    {
        _pouches = pouches;
        return PatchTargets.MapInsuredItemsToTrader;
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, PmcData pmcProfile, List<InsuranceEquipmentPkg> __result)
    {
        if (__result != null)
        {
            _pouches.AddToPackages(sessionId, __result, pmcProfile);
        }
    }
}
