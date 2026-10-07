using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Services;

namespace ModularVests.Server.Services;

/// <summary>
/// Insurance for a modular rig covers the pouches hung on it, the way the server's own
/// insurance covers the soft armor of body armor: a lost insured rig comes back with every
/// pouch it carried, insured or not, and the trader never keeps one back.
///
/// The pouches are read from the profile as it stood before the raid. They cannot be taken off
/// or hung on in raid, so that is what the rig carried when it was lost - on the body or dropped
/// somewhere on the map, where the post-raid inventory no longer has it.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class InsuredPouches
{
    private readonly ConcurrentDictionary<MongoId, List<Item>> _preRaid = new();

    /// <summary>Remembers the pouches of a profile before the raid's results are applied to it.</summary>
    public void Remember(MongoId sessionId, IEnumerable<Item>? inventory) =>
        _preRaid[sessionId] = PouchesIn(inventory).Select(Copy).ToList();

    /// <summary>
    /// Adds the pouches of every lost rig to the insurance packages. Returns how many were added.
    /// </summary>
    public int AddToPackages(MongoId sessionId, List<InsuranceEquipmentPkg> packages, PmcData? profile)
    {
        var pouches = _preRaid.TryRemove(sessionId, out var remembered)
            ? remembered
            : PouchesIn(profile?.Inventory?.Items).ToList();
        return Complete(packages, pouches, sessionId, profile);
    }

    /// <summary>
    /// Every pouch whose rig is being returned goes back with it, from the rig's trader - an
    /// insured pouch too, if it was insured with somebody else: it would come back in another
    /// package, on its own. Returns how many packages were added.
    /// </summary>
    internal static int Complete(List<InsuranceEquipmentPkg> packages, IEnumerable<Item> pouches,
        MongoId sessionId, PmcData? profile)
    {
        var rigs = packages
            .Where(package => package.ItemToReturnToPlayer != null)
            .GroupBy(package => package.ItemToReturnToPlayer!.Id.ToString())
            .ToDictionary(group => group.Key, group => group.First().TraderId);
        var added = 0;

        foreach (var pouch in pouches)
        {
            if (pouch.ParentId == null || !rigs.TryGetValue(pouch.ParentId, out var trader))
            {
                continue; // its rig was not lost, or was not insured
            }

            var existing = packages.FirstOrDefault(package => package.ItemToReturnToPlayer?.Id == pouch.Id);
            if (existing != null)
            {
                existing.TraderId = trader;
                continue;
            }

            packages.Add(new InsuranceEquipmentPkg
            {
                SessionId = sessionId,
                PmcData = profile,
                ItemToReturnToPlayer = Copy(pouch),
                TraderId = trader,
            });
            added++;
        }

        return added;
    }

    /// <summary>
    /// Takes the pouches out of the attachments a trader may keep back when a package comes
    /// home: they cannot be changed in raid, like the soft armor the server already leaves out.
    /// </summary>
    internal static Dictionary<MongoId, List<Item>> WithoutPouches(Dictionary<MongoId, List<Item>> attachments) =>
        attachments
            .Select(entry => (entry.Key, Attachments: entry.Value.Where(item => !IsPouch(item)).ToList()))
            .Where(entry => entry.Attachments.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Attachments);

    internal static bool IsPouch(Item item) =>
        item.SlotId?.StartsWith(ClusterGrid.SlotPrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<Item> PouchesIn(IEnumerable<Item>? items) => items?.Where(IsPouch) ?? [];

    private static Item Copy(Item item) => new()
    {
        Id = item.Id,
        Template = item.Template,
        ParentId = item.ParentId,
        SlotId = item.SlotId,
        Upd = item.Upd,
    };
}
