using System.Collections.Generic;
using EFT.InventoryLogic;

namespace ModularVests.Client
{
    /// <summary>
    /// The pouches of a rig in the order its window shows them: row by row, left to right
    /// (RigWindowPatch records it on every build). What spills over from one pouch goes on in this
    /// order - into the pouch next to it on screen - rather than in the order of the rig's cells,
    /// which the window regroups by kind.
    /// </summary>
    internal static class PouchOrder
    {
        private static readonly Dictionary<string, List<string>> Shown = new Dictionary<string, List<string>>();

        public static void Record(CompoundItem rig, List<CompoundItem> pouches)
        {
            if (rig?.Id == null)
            {
                return;
            }

            Shown[rig.Id] = pouches.ConvertAll(p => p.Id);
        }

        /// <summary>
        /// The rig's attached pouches to try after <paramref name="from"/> (or all of them, from the first,
        /// when it is null): the ones after it on screen, then the ones before it; pouches the window has
        /// not shown yet follow in cell order.
        /// </summary>
        public static List<CompoundItem> After(CompoundItem rig, CompoundItem from)
        {
            var attached = new List<CompoundItem>(PouchSlots.AttachedPouches(rig));
            if (!Shown.TryGetValue(rig.Id, out var shown))
            {
                return Rotate(attached, from);
            }

            var byId = new Dictionary<string, CompoundItem>();
            foreach (var pouch in attached)
            {
                byId[pouch.Id] = pouch;
            }

            var ordered = new List<CompoundItem>();
            foreach (var id in shown)
            {
                if (byId.TryGetValue(id, out var pouch))
                {
                    ordered.Add(pouch);
                    byId.Remove(id);
                }
            }

            var result = Rotate(ordered, from);
            foreach (var pouch in attached)
            {
                if (byId.ContainsKey(pouch.Id))
                {
                    result.Add(pouch);
                }
            }

            return result;
        }

        /// <summary>The list from just after <paramref name="from"/>, wrapping round, without it.</summary>
        private static List<CompoundItem> Rotate(List<CompoundItem> pouches, CompoundItem from)
        {
            var at = from == null ? -1 : pouches.IndexOf(from);
            var result = new List<CompoundItem>(pouches.Count);
            for (var i = 1; i <= pouches.Count; i++)
            {
                var pouch = pouches[(at + i + pouches.Count) % pouches.Count];
                if (pouch != from)
                {
                    result.Add(pouch);
                }
            }

            return result;
        }
    }
}
