#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace ModularVests.DevTools.Geometry
{
    /// <summary>
    /// Names that say which cell is which, so cells and clusters do not get mixed up while laying
    /// them out: cluster names, cell names, short labels and the one-line passport of a cell.
    /// </summary>
    internal static class CellInfo
    {
        /// <summary>
        /// Growth directions of a pouch in its anchor cell's own axes: to the right as seen from
        /// the front and down. A cell's +Z faces out of the rig, so from the front its +X is on
        /// the viewer's left (the wearer's right): "right" is -X.
        /// </summary>
        public static readonly Vec3 GrowRight = new Vec3(-1f, 0f, 0f);

        public static readonly Vec3 GrowDown = new Vec3(0f, -1f, 0f);

        /// <summary>"chest L", "cummerbund R"... (the wearer's sides).</summary>
        public static string ClusterName(int cluster)
        {
            var pair = (cluster - 1) / 2;
            var side = cluster % 2 == 1 ? "L" : "R";
            switch (pair)
            {
                case 0: return "chest " + side;
                case 1: return "cummerbund " + side;
                default: return $"pair {pair + 1} {side}";
            }
        }

        public static string CellName(int position)
        {
            switch (position)
            {
                case 1: return "top-left";
                case 2: return "top-right";
                case 3: return "bottom-left";
                case 4: return "bottom-right";
                default: return "?";
            }
        }

        /// <summary>"3.2": cluster 3, cell 2.</summary>
        public static string Label(int cluster, int position) => $"{cluster}.{position}";

        /// <summary>
        /// Everything about a cell in one line, e.g.
        /// <c>C3 · cummerbund L · cell 2 (top-right) · mod_pouch_10 · accepts 1x2, 1x1 · mirror → C4 cell 1 (mod_pouch_13)</c>.
        /// </summary>
        public static string Passport(string slotName)
        {
            if (!ClusterGrid.TryParse(slotName, out var cluster, out var position))
            {
                return slotName + " · not a pouch cell";
            }

            var mirrorCluster = ClusterGrid.MirrorCluster(cluster);
            var mirrorPosition = ClusterGrid.MirrorPosition(position);
            var accepts = string.Join(", ", ClusterGrid.Accepted(position).Select(f => f.ToString()).ToArray());
            return $"C{cluster} · {ClusterName(cluster)} · cell {position} ({CellName(position)}) · {slotName} · " +
                   $"accepts {accepts} · mirror → C{mirrorCluster} cell {mirrorPosition} " +
                   $"({ClusterGrid.SlotName(mirrorCluster, mirrorPosition)})";
        }

        /// <summary>
        /// Whether a cluster reads the right way round, judged in cell 1's own axes: cell 2 must
        /// lie to the right of cell 1 and cell 3 below it. Positions in any one space; the axes
        /// are cell 1's +X and +Y in that space.
        /// </summary>
        public static List<string> OrderWarnings(int cluster, Vec3 cell1, Vec3 cell1X, Vec3 cell1Y, Vec3? cell2,
            Vec3? cell3)
        {
            var warnings = new List<string>();
            var right = cell1X * GrowRight.X;
            var down = cell1Y * GrowDown.Y;
            if (cell2.HasValue && Vec3.Dot(cell2.Value - cell1, right) <= 0f)
            {
                warnings.Add($"C{cluster}: cell 2 is not to the right of cell 1");
            }

            if (cell3.HasValue && Vec3.Dot(cell3.Value - cell1, down) <= 0f)
            {
                warnings.Add($"C{cluster}: cell 3 is not below cell 1");
            }

            return warnings;
        }
    }
}
