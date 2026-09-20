using System.Collections.Generic;
using EFT.InventoryLogic;

namespace ModularVests.Client
{
    /// <summary>
    /// What makes a slot a pouch slot: its id prefix, and nothing else. The server's config
    /// validation enforces the same prefix (VestConfig.PouchSlotPrefix). Which cell of which
    /// cluster a slot is, and what a pouch covers from it, is <see cref="ClusterGrid"/>.
    /// </summary>
    internal static class PouchSlots
    {
        public const string Prefix = ClusterGrid.SlotPrefix;

        public static bool IsPouchSlot(Slot slot) =>
            slot != null && slot.ID != null && slot.ID.StartsWith(Prefix, System.StringComparison.Ordinal);

        public static bool IsPouchSlot(IContainer container) => container is Slot slot && IsPouchSlot(slot);

        /// <summary>The pouch slots of a rig, in template order.</summary>
        public static List<Slot> Of(CompoundItem rig)
        {
            var result = new List<Slot>();
            var slots = rig?.Slots;
            if (slots == null)
            {
                return result;
            }

            foreach (var slot in slots)
            {
                if (IsPouchSlot(slot))
                {
                    result.Add(slot);
                }
            }

            return result;
        }

        /// <summary>True when the rig has at least one pouch slot.</summary>
        public static bool HasAny(CompoundItem rig)
        {
            var slots = rig?.Slots;
            if (slots == null)
            {
                return false;
            }

            foreach (var slot in slots)
            {
                if (IsPouchSlot(slot))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The pouches attached to a rig (containers only), in slot order.</summary>
        public static IEnumerable<CompoundItem> AttachedPouches(CompoundItem rig)
        {
            var slots = rig?.Slots;
            if (slots == null)
            {
                yield break;
            }

            foreach (var slot in slots)
            {
                if (IsPouchSlot(slot) && slot.ContainedItem is CompoundItem pouch)
                {
                    yield return pouch;
                }
            }
        }

        /// <summary>
        /// The slot holding this pouch, when the item is a pouch on a rig; null otherwise.
        /// </summary>
        public static Slot SlotOf(Item item) =>
            item?.CurrentAddress?.Container is Slot slot && IsPouchSlot(slot) ? slot : null;

        // --- clusters ---

        /// <summary>Cluster of a pouch slot (from 1), 0 when the slot is not a cell.</summary>
        public static int ClusterOf(Slot slot) => ClusterGrid.TryParse(slot?.ID, out var cluster, out _) ? cluster : 0;

        /// <summary>Cell of a pouch slot in its cluster (1..4), 0 when the slot is not a cell.</summary>
        public static int PositionOf(Slot slot) => ClusterGrid.TryParse(slot?.ID, out _, out var position) ? position : 0;

        /// <summary>
        /// The four cells of a cluster, indexed by position (element 0 unused; a cell the rig
        /// lacks is null).
        /// </summary>
        public static Slot[] ClusterSlots(CompoundItem rig, int cluster)
        {
            var result = new Slot[ClusterGrid.CellsPerCluster + 1];
            foreach (var slot in rig?.Slots ?? System.Array.Empty<Slot>())
            {
                if (ClusterGrid.TryParse(slot.ID, out var c, out var position) && c == cluster)
                {
                    result[position] = slot;
                }
            }

            return result;
        }

        private static readonly Dictionary<string, Footprint?> Footprints = new Dictionary<string, Footprint?>();

        /// <summary>
        /// The pouch's footprint as the rig sees it: read back from which cells accept it
        /// (<see cref="ClusterGrid.TryInferFootprint"/>), cached per rig and pouch template.
        /// Null when the rig has no cell that accepts the item.
        /// </summary>
        public static Footprint? FootprintOf(Item item, CompoundItem rig)
        {
            if (item == null || rig == null)
            {
                return null;
            }

            var key = rig.StringTemplateId + ":" + item.StringTemplateId;
            if (Footprints.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // every cluster of a rig accepts the same, the first complete one is enough
            Footprint? result = null;
            for (var cluster = 1; ; cluster++)
            {
                var cells = ClusterSlots(rig, cluster);
                if (cells[1] == null)
                {
                    break;
                }

                if (cells[2] == null || cells[3] == null || cells[4] == null)
                {
                    continue;
                }

                if (ClusterGrid.TryInferFootprint(p => cells[p].CheckCompatibility(item), out var footprint))
                {
                    result = footprint;
                }

                break;
            }

            Footprints[key] = result;
            return result;
        }

        /// <summary>
        /// The other cells a pouch covers when attached to this slot: the slots it blocks. Taken
        /// from the slot's own neighbour map (Slot.ConflictingSlots, filled when the rig is
        /// built), so it is exactly what the game's blocking works with.
        /// </summary>
        public static List<Slot> CoveredSlots(Slot slot, Item item)
        {
            var result = new List<Slot>();
            if (!ClusterGrid.TryParse(slot?.ID, out var cluster, out var position) || slot.ConflictingSlots == null)
            {
                return result;
            }

            var footprint = FootprintOf(item, slot.ParentItem as CompoundItem);
            if (footprint == null)
            {
                return result;
            }

            foreach (var cell in ClusterGrid.Covered(position, footprint.Value))
            {
                if (slot.ConflictingSlots.TryGetValue(ClusterGrid.SlotName(cluster, cell), out var covered))
                {
                    result.Add(covered);
                }
            }

            return result;
        }
    }
}
