using System.Reflection;
using ModularVests.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ModularVests.Server.Patches;

/// <summary>
/// A trader returning insurance may keep some attachments back, but only those that can be
/// changed in raid. Pouches cannot, so they join the soft armor the server already leaves out.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PouchesKeptPatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod() => PatchTargets.RemoveNonModdableAttachments;

    [PatchPostfix]
    public static void Postfix(ref Dictionary<MongoId, List<Item>> __result)
    {
        if (__result != null)
        {
            __result = InsuredPouches.WithoutPouches(__result);
        }
    }
}
