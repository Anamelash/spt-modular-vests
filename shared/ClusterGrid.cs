#nullable disable
using System;
using System.Collections.Generic;

namespace ModularVests
{
    /// <summary>
    /// Size of a pouch on a rig, in cells: <see cref="Width"/> columns by <see cref="Height"/>
    /// rows. Written as "WxH" ("1x2" is one column, two rows).
    /// </summary>
    internal readonly struct Footprint : IEquatable<Footprint>
    {
        public readonly int Width;
        public readonly int Height;

        public Footprint(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public static readonly Footprint F1x1 = new Footprint(1, 1);
        public static readonly Footprint F1x2 = new Footprint(1, 2);
        public static readonly Footprint F2x1 = new Footprint(2, 1);
        public static readonly Footprint F2x2 = new Footprint(2, 2);

        /// <summary>Every footprint a pouch can have.</summary>
        public static readonly Footprint[] All = { F1x1, F1x2, F2x1, F2x2 };

        public static bool TryParse(string text, out Footprint footprint)
        {
            footprint = default;
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    footprint = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool Equals(Footprint other) => Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is Footprint other && Equals(other);

        public override int GetHashCode() => Width * 31 + Height;

        public static bool operator ==(Footprint a, Footprint b) => a.Equals(b);

        public static bool operator !=(Footprint a, Footprint b) => !a.Equals(b);

        public override string ToString() => $"{Width}x{Height}";
    }

    /// <summary>
    /// Grammar and geometry of pouch slots, shared by the server, the client and the tests.
    ///
    /// A rig's pouch slots come in clusters of four cells, numbered the way they read:
    /// <code>
    /// 1 2
    /// 3 4
    /// </code>
    /// Slot <c>mod_pouch_N</c> (N from 1) is cell <c>(N-1) % 4 + 1</c> of cluster
    /// <c>(N-1) / 4 + 1</c>. A pouch sits in its anchor cell and spreads right and down from it;
    /// the rectangle it covers must stay inside the cluster. Clusters come in mirrored pairs
    /// (1-2, 3-4, ...); mirroring swaps the columns (cell 1 with 2, 3 with 4), so a pouch always
    /// grows towards cell 2 on either side.
    ///
    /// INVARIANT: slot names never change meaning. A rig's clusters are only ever added.
    /// </summary>
    internal static class ClusterGrid
    {
        /// <summary>The only thing that marks a pouch slot, on both sides.</summary>
        public const string SlotPrefix = "mod_pouch_";

        public const int Columns = 2;
        public const int Rows = 2;
        public const int CellsPerCluster = Columns * Rows;

        public static string SlotName(int number)
        {
            if (number < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(number), number, "slot numbers start at 1");
            }

            return SlotPrefix + number;
        }

        public static string SlotName(int cluster, int position) => SlotName(SlotNumber(cluster, position));

        /// <summary>The cluster slot number N belongs to.</summary>
        public static int ClusterOfSlot(int number) => (number - 1) / CellsPerCluster + 1;

        /// <summary>The cell inside its cluster that slot number N is.</summary>
        public static int PositionOfSlot(int number) => (number - 1) % CellsPerCluster + 1;

        public static int SlotNumber(int cluster, int position)
        {
            if (cluster < 1 || position < 1 || position > CellsPerCluster)
            {
                throw new ArgumentOutOfRangeException(nameof(position), $"cluster {cluster}, cell {position}");
            }

            return (cluster - 1) * CellsPerCluster + position;
        }

        /// <summary>
        /// Reads "mod_pouch_N". Only the canonical spelling is accepted: no sign, no leading
        /// zeros, nothing after the number.
        /// </summary>
        public static bool TryParse(string slotName, out int cluster, out int position)
        {
            cluster = 0;
            position = 0;
            if (slotName == null || !slotName.StartsWith(SlotPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var digits = slotName.Substring(SlotPrefix.Length);
            if (digits.Length == 0 || digits.Length > 6 || digits[0] == '0')
            {
                return false;
            }

            var number = 0;
            foreach (var c in digits)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }

                number = number * 10 + (c - '0');
            }

            cluster = (number - 1) / CellsPerCluster + 1;
            position = (number - 1) % CellsPerCluster + 1;
            return true;
        }

        /// <summary>Column of a cell, from 0 (left).</summary>
        public static int ColumnOf(int position) => (position - 1) % Columns;

        /// <summary>Row of a cell, from 0 (top).</summary>
        public static int RowOf(int position) => (position - 1) / Columns;

        public static int PositionAt(int column, int row) => row * Columns + column + 1;

        /// <summary>Whether a pouch of this size can be anchored in this cell.</summary>
        public static bool Fits(int position, Footprint footprint)
        {
            if (position < 1 || position > CellsPerCluster || footprint.Width < 1 || footprint.Height < 1)
            {
                return false;
            }

            return ColumnOf(position) + footprint.Width <= Columns && RowOf(position) + footprint.Height <= Rows;
        }

        /// <summary>
        /// The cells a pouch anchored here covers besides its anchor, in reading order. Empty
        /// when it does not fit.
        /// </summary>
        public static int[] Covered(int position, Footprint footprint)
        {
            if (!Fits(position, footprint))
            {
                return Array.Empty<int>();
            }

            var result = new List<int>();
            for (var row = RowOf(position); row < RowOf(position) + footprint.Height; row++)
            {
                for (var column = ColumnOf(position); column < ColumnOf(position) + footprint.Width; column++)
                {
                    var cell = PositionAt(column, row);
                    if (cell != position)
                    {
                        result.Add(cell);
                    }
                }
            }

            return result.ToArray();
        }

        /// <summary>The anchor and the covered cells together.</summary>
        public static int[] Occupied(int position, Footprint footprint)
        {
            if (!Fits(position, footprint))
            {
                return Array.Empty<int>();
            }

            var covered = Covered(position, footprint);
            var result = new int[covered.Length + 1];
            result[0] = position;
            covered.CopyTo(result, 1);
            return result;
        }

        /// <summary>The footprints this cell accepts, largest first.</summary>
        public static IEnumerable<Footprint> Accepted(int position)
        {
            for (var i = Footprint.All.Length - 1; i >= 0; i--)
            {
                if (Fits(position, Footprint.All[i]))
                {
                    yield return Footprint.All[i];
                }
            }
        }

        /// <summary>The same cell on the mirrored cluster: the columns swap, the rows stay.</summary>
        public static int MirrorPosition(int position) =>
            PositionAt(Columns - 1 - ColumnOf(position), RowOf(position));

        /// <summary>The paired cluster: 1-2, 3-4, ...</summary>
        public static int MirrorCluster(int cluster) => cluster % 2 == 1 ? cluster + 1 : cluster - 1;

        /// <summary>
        /// A pouch's footprint as the slots see it: the most restricted cell that accepts it
        /// tells its size. Cell 4 takes only 1x1; cell 2 takes 1x2 and smaller; cell 3 takes
        /// 2x1 and smaller; cell 1 takes everything. False when no cell accepts it.
        /// </summary>
        public static bool TryInferFootprint(Func<int, bool> acceptedAt, out Footprint footprint)
        {
            if (acceptedAt(4))
            {
                footprint = Footprint.F1x1;
            }
            else if (acceptedAt(2))
            {
                footprint = Footprint.F1x2;
            }
            else if (acceptedAt(3))
            {
                footprint = Footprint.F2x1;
            }
            else if (acceptedAt(1))
            {
                footprint = Footprint.F2x2;
            }
            else
            {
                footprint = default;
                return false;
            }

            return true;
        }
    }
}
