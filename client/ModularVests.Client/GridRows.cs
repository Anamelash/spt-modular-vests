#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModularVests.Client
{
    /// <summary>
    /// Splits the grids of a rig window into rows no wider than the window. Plain data, no Unity:
    /// the tests run it. Every row is centred by the window.
    ///
    /// The unit is a pouch (all of its grids), never torn unless it is wider than a row itself.
    /// Pouches are sorted by kind - the shape of their sections, so the colours of a model and any
    /// other pouch with the same sections are one kind - and each kind is split evenly into the
    /// fewest rows it needs (4+4 rather than 6+2), wider rows first. Rows of different kinds are
    /// then joined while the result fits and reads symmetric: at most one kind with an odd count,
    /// which takes the centre, every other kind split in halves on both sides, larger pouches
    /// nearer the centre (1x1 1x1 2x2 1x1 1x1). A pouch whose sections are shown in a column
    /// (stacked) is as wide as its widest section.
    ///
    /// A kind the rig carries only once cannot be symmetric anyway, so it does not stand in the way
    /// of a join: single pouches of different kinds share a row, from the centre outwards, widest
    /// first. Such a row is not balanced, so it goes after the rows that are (a rig of one pouch of
    /// each kind used to be a staircase of one-pouch rows).
    /// </summary>
    internal static class GridRows
    {
        /// <param name="widths">Width of each grid, in cells.</param>
        /// <param name="groups">Which pouch each grid belongs to (equal neighbouring values = one pouch).</param>
        /// <param name="kinds">Kind of each grid's pouch: pouches of one kind look the same.</param>
        /// <param name="maxWidth">Widest row, in cells. A grid wider than that gets a row of its own.</param>
        /// <returns>The rows, each a list of indexes into <paramref name="widths"/>.</returns>
        public static List<List<int>> Layout(IList<int> widths, IList<int> groups, IList<string> kinds, int maxWidth) =>
            Layout(widths, groups, kinds, null, maxWidth);

        /// <param name="stacked">
        /// Per grid: its pouch shows its sections in a column (one above the other) rather than side
        /// by side, so the pouch is as wide as its widest section. Null: none is.
        /// </param>
        public static List<List<int>> Layout(IList<int> widths, IList<int> groups, IList<string> kinds,
            IList<bool> stacked, int maxWidth)
        {
            maxWidth = Math.Max(1, maxWidth);
            var units = Units(widths, groups, kinds, stacked);

            var rows = new List<Row>();
            foreach (var kind in units.Select(u => u.Kind).Distinct().ToList())
            {
                var ofKind = units.Where(u => u.Kind == kind).ToList();
                foreach (var part in Split(ofKind.ConvertAll(u => u.Width), maxWidth))
                {
                    var first = ofKind[part[0]];
                    if (part.Count == 1 && first.Width > maxWidth)
                    {
                        // a pouch wider than a row: its grids over the rows it needs, torn (a
                        // column is torn into one section a row)
                        var pieces = first.Stacked
                            ? first.Grids.ConvertAll(k => new List<int> { first.Grids.IndexOf(k) })
                            : Split(first.Grids.ConvertAll(i => Width(widths[i])), maxWidth);
                        foreach (var gridPart in pieces)
                        {
                            rows.Add(new Row(gridPart.ConvertAll(k => first.Grids[k])));
                        }

                        continue;
                    }

                    rows.Add(new Row(part.ConvertAll(k => ofKind[k])));
                }
            }

            var singleKinds = new HashSet<string>(units.GroupBy(u => u.Kind).Where(g => g.Count() == 1)
                .Select(g => g.Key));
            Join(rows, singleKinds, maxWidth);

            // the rows that read symmetric first, the rest in the order they were built
            var ordered = rows.Where(r => r.Balanced).Concat(rows.Where(r => !r.Balanced));
            return ordered.Select(r => r.Arrange()).ToList();
        }

        private static int Width(int width) => Math.Max(1, width);

        private sealed class Unit
        {
            public string Kind;
            public List<int> Grids;
            public int Width;
            public bool Stacked;
        }

        private static List<Unit> Units(IList<int> widths, IList<int> groups, IList<string> kinds, IList<bool> stacked)
        {
            var units = new List<Unit>();
            for (var i = 0; i < widths.Count; i++)
            {
                if (i == 0 || groups[i] != groups[i - 1])
                {
                    units.Add(new Unit
                    {
                        Kind = kinds[i] ?? string.Empty,
                        Grids = new List<int>(),
                        Stacked = stacked != null && stacked[i],
                    });
                }

                var unit = units[units.Count - 1];
                unit.Grids.Add(i);
                unit.Width = unit.Stacked ? Math.Max(unit.Width, Width(widths[i])) : unit.Width + Width(widths[i]);
            }

            return units;
        }

        private sealed class Row
        {
            /// <summary>Whole pouches; null for the pieces of a torn one.</summary>
            public readonly List<Unit> Units;

            private readonly List<int> _torn;

            public Row(List<Unit> units)
            {
                Units = units;
            }

            public Row(List<int> tornGrids)
            {
                _torn = tornGrids;
            }

            public int Width => Units?.Sum(u => u.Width) ?? int.MaxValue;

            /// <summary>Reads symmetric: at most one kind of it comes in an odd number.</summary>
            public bool Balanced =>
                Units == null || Units.GroupBy(u => u.Kind).Count(g => g.Count() % 2 == 1) <= 1;

            public List<int> Arrange()
            {
                if (Units == null)
                {
                    return _torn;
                }

                var byKind = Units.OrderBy(u => u.Grids[0]).GroupBy(u => u.Kind).Select(g => g.ToList()).ToList();
                var alone = byKind.Where(k => k.Count == 1).Select(k => k[0])
                    .OrderByDescending(u => u.Width).ToList();

                // the centre: the one kind that comes in an odd number, else the widest pouch that
                // is here on its own, else (an even number of every kind) the widest kind
                var centre = byKind.FirstOrDefault(k => k.Count > 1 && k.Count % 2 == 1);
                if (centre == null && alone.Count > 0)
                {
                    centre = new List<Unit> { alone[0] };
                    alone.RemoveAt(0);
                }

                centre = centre ?? byKind.OrderByDescending(k => k[0].Width).First();

                // nearest the centre first, on both sides
                var left = new List<Unit>();
                var right = new List<Unit>();
                foreach (var ring in byKind.Where(k => k != centre && k.Count > 1)
                             .OrderByDescending(k => k[0].Width))
                {
                    left.AddRange(ring.Take(ring.Count / 2));
                    right.AddRange(ring.Skip(ring.Count / 2));
                }

                // pouches that are here on their own: widest nearest the centre, side by side
                for (var i = 0; i < alone.Count; i++)
                {
                    (i % 2 == 0 ? right : left).Add(alone[i]);
                }

                left.Reverse();
                var order = left.Concat(centre).Concat(right);
                return order.SelectMany(u => u.Grids).ToList();
            }
        }

        /// <summary>
        /// Joins rows while one fits into another symmetrically, the narrowest join first (it keeps
        /// the rows even). Each join saves a row. A kind the rig carries only once
        /// (<paramref name="singleKinds"/>) never breaks the symmetry: it cannot be paired anyway.
        /// </summary>
        private static void Join(List<Row> rows, HashSet<string> singleKinds, int maxWidth)
        {
            while (true)
            {
                int bestI = -1, bestJ = -1, bestWidth = int.MaxValue;
                for (var i = 0; i < rows.Count; i++)
                {
                    for (var j = i + 1; j < rows.Count; j++)
                    {
                        if (rows[i].Units == null || rows[j].Units == null)
                        {
                            continue;
                        }

                        var width = rows[i].Width + rows[j].Width;
                        if (width > maxWidth || width >= bestWidth)
                        {
                            continue;
                        }

                        var odd = rows[i].Units.Concat(rows[j].Units)
                            .Where(u => !singleKinds.Contains(u.Kind))
                            .GroupBy(u => u.Kind)
                            .Count(g => g.Count() % 2 == 1);
                        if (odd <= 1)
                        {
                            bestI = i;
                            bestJ = j;
                            bestWidth = width;
                        }
                    }
                }

                if (bestI < 0)
                {
                    return;
                }

                rows[bestI] = new Row(rows[bestI].Units.Concat(rows[bestJ].Units).ToList());
                rows.RemoveAt(bestJ);
            }
        }

        /// <summary>
        /// Cuts a sequence into rows no wider than <paramref name="maxWidth"/>, keeping its order:
        /// the fewest rows, then the most even (least sum of squared widths), then wider rows first.
        /// A single item wider than a row gets one of its own.
        /// </summary>
        private static List<List<int>> Split(List<int> widths, int maxWidth)
        {
            var n = widths.Count;
            var rows = new List<List<int>>();
            if (n == 0)
            {
                return rows;
            }

            // best[i] = the best way to lay out the first i items; prev[i] = where its last row starts
            var best = new Cost[n + 1];
            var prev = new int[n + 1];
            best[0] = Cost.Zero;
            for (var i = 1; i <= n; i++)
            {
                best[i] = Cost.Infinite;
                var width = 0;
                for (var start = i - 1; start >= 0; start--)
                {
                    width += Width(widths[start]);
                    if (width > maxWidth && start < i - 1)
                    {
                        break; // only a single item may overflow a row
                    }

                    if (best[start].IsInfinite)
                    {
                        continue;
                    }

                    var candidate = best[start].Add(width);
                    if (candidate.IsBetterThan(best[i]))
                    {
                        best[i] = candidate;
                        prev[i] = start;
                    }
                }
            }

            for (var end = n; end > 0; end = prev[end])
            {
                var row = new List<int>();
                for (var i = prev[end]; i < end; i++)
                {
                    row.Add(i);
                }

                rows.Insert(0, row);
            }

            return rows;
        }

        /// <summary>The objective, compared field by field.</summary>
        private readonly struct Cost
        {
            private readonly int _rows;
            private readonly int _squares;

            /// <summary>How much the rows grow on the way down (a row wider than the one before it).</summary>
            private readonly int _order;

            private readonly int _lastWidth;
            private readonly bool _infinite;

            private Cost(int rows, int squares, int order, int lastWidth, bool infinite)
            {
                _rows = rows;
                _squares = squares;
                _order = order;
                _lastWidth = lastWidth;
                _infinite = infinite;
            }

            public static Cost Zero => new Cost(0, 0, 0, int.MaxValue, false);

            public static Cost Infinite => new Cost(0, 0, 0, 0, true);

            public bool IsInfinite => _infinite;

            public Cost Add(int width) => new Cost(
                _rows + 1, _squares + width * width, _order + Math.Max(0, width - _lastWidth), width, false);

            public bool IsBetterThan(Cost other)
            {
                if (other._infinite)
                {
                    return !_infinite;
                }

                if (_infinite)
                {
                    return false;
                }

                if (_rows != other._rows)
                {
                    return _rows < other._rows;
                }

                if (_squares != other._squares)
                {
                    return _squares < other._squares;
                }

                return _order < other._order;
            }
        }
    }
}
