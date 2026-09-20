using ModularVests.Client;
using Xunit;

namespace ModularVests.Server.Tests;

public class GridRowsTests
{
    /// <summary>A pouch: its kind and the widths of its grids.</summary>
    private sealed record Pouch(string Kind, params int[] Grids);

    private static Pouch P(string kind, params int[] grids) => new(kind, grids);

    /// <summary>Lays pouches out; each grid is written as its pouch's kind, e.g. "G M M G | M M".</summary>
    private static string Layout(int max, params Pouch[] pouches)
    {
        var (widths, groups, kinds) = Flatten(pouches);
        return string.Join(" | ", GridRows.Layout(widths, groups, kinds, max)
            .Select(row => string.Join(" ", row.Select(i => kinds[i]))));
    }

    /// <summary>Rows as their widths, e.g. "3+3 | 2+2".</summary>
    private static string Widths(int max, params Pouch[] pouches)
    {
        var (widths, groups, kinds) = Flatten(pouches);
        return string.Join(" | ", GridRows.Layout(widths, groups, kinds, max)
            .Select(row => string.Join("+", row.Select(i => widths[i]))));
    }

    private static (int[] Widths, int[] Groups, string[] Kinds) Flatten(Pouch[] pouches)
    {
        var widths = pouches.SelectMany(p => p.Grids).ToArray();
        var groups = pouches.SelectMany((p, i) => p.Grids.Select(_ => i)).ToArray();
        var kinds = pouches.SelectMany(p => p.Grids.Select(_ => p.Kind)).ToArray();
        return (widths, groups, kinds);
    }

    private static Pouch[] Many(int count, string kind, params int[] grids) =>
        Enumerable.Range(0, count).Select(_ => P(kind, grids)).ToArray();

    [Fact]
    public void Everything_that_fits_stays_on_one_row()
    {
        Assert.Equal("2+2+2", Widths(6, Many(3, "a", 2)));
        Assert.Equal("", Widths(6));
    }

    [Fact]
    public void Rows_of_a_kind_are_even_rather_than_filled()
    {
        // four double mag pouches: 4+4, not 6+2
        Assert.Equal("1+1+1+1 | 1+1+1+1", Widths(6, Many(4, "M", 1, 1)));
        Assert.Equal("1+1+1+1 | 1+1+1", Widths(6, Many(7, "a", 1)));
    }

    [Fact]
    public void Wider_rows_come_first_when_equally_even()
    {
        Assert.Equal("2+2+1 | 2+2", Widths(6, P("a", 2), P("a", 2), P("a", 1), P("a", 2), P("a", 2)));
    }

    [Fact]
    public void Small_pouches_flank_a_row_of_larger_ones()
    {
        // the rig of the report: four mag pouches and two grenade pouches between them
        var pouches = new[] { P("M", 1, 1), P("G", 1), P("M", 1, 1), P("M", 1, 1), P("G", 1), P("M", 1, 1) };
        Assert.Equal("G M M M M G | M M M M", Layout(6, pouches));
    }

    [Fact]
    public void A_lone_pouch_takes_the_centre()
    {
        var pouches = new[] { P("G", 1), P("G", 1), P("S", 2), P("G", 1), P("G", 1) };
        Assert.Equal("G G S G G", Layout(6, pouches));
        Assert.Equal("G G S G G", Layout(6, P("S", 2), P("G", 1), P("G", 1), P("G", 1), P("G", 1)));
    }

    [Fact]
    public void A_kind_the_rig_carries_once_joins_another_row()
    {
        // one survival and three grenade pouches: the survival cannot be paired, so it joins
        Assert.Equal("G G G S", Layout(6, P("S", 2), P("G", 1), P("G", 1), P("G", 1)));

        // two of each: neither can be paired off against the other, so they keep their rows
        Assert.Equal("S S | G G G G", Layout(4, [P("S", 2), P("S", 2), .. Many(4, "G", 1)]));
    }

    [Fact]
    public void Kinds_are_not_mixed_beyond_the_width()
    {
        Assert.Equal("M M M M M M | G G", Layout(6, Many(3, "M", 1, 1).Concat(Many(2, "G", 1)).ToArray()));
    }

    [Fact]
    public void Nothing_is_lost_and_rows_fit()
    {
        var pouches = new[]
        {
            P("M", 1, 1), P("G", 1), P("S", 2, 2), P("A", 2, 2), P("M", 1, 1), P("G", 1), P("S", 2, 2), P("G", 1),
        };
        var (widths, groups, kinds) = Flatten(pouches);
        var rows = GridRows.Layout(widths, groups, kinds, 6);
        Assert.Equal(Enumerable.Range(0, widths.Length), rows.SelectMany(r => r).Order());
        Assert.All(rows, row => Assert.True(row.Sum(i => widths[i]) <= 6));

        // no pouch is torn
        foreach (var row in rows)
        {
            Assert.All(row, i => Assert.All(Enumerable.Range(0, widths.Length).Where(j => groups[j] == groups[i]),
                j => Assert.Contains(j, row)));
        }
    }

    [Fact]
    public void A_pouch_wider_than_a_row_is_torn_evenly()
    {
        Assert.Equal("2+2 | 2+2 | 1", Widths(6, P("W", 2, 2, 2, 2), P("a", 1)));
    }

    [Fact]
    public void A_stacked_pouch_is_as_wide_as_its_widest_section()
    {
        // three admin pouches (2x1 + 2x1 in a column): 2 cells each, all on one row; side by
        // side they would be 4 cells each and need three rows
        var widths = Enumerable.Repeat(2, 6).ToArray();
        int[] groups = [0, 0, 1, 1, 2, 2];
        var kinds = Enumerable.Repeat("A", 6).ToArray();
        var stacked = Enumerable.Repeat(true, 6).ToArray();
        var rows = GridRows.Layout(widths, groups, kinds, stacked, 6);
        Assert.Equal([0, 1, 2, 3, 4, 5], Assert.Single(rows));

        Assert.Equal(3, GridRows.Layout(widths, groups, kinds, 6).Count);
    }

    [Fact]
    public void Stacked_and_side_by_side_pouches_share_a_symmetric_row()
    {
        // mag pouch (1x2 + 1x2 side by side, 2 wide) between two gadget pouches (1x1 + 1x1 stacked, 1 wide)
        int[] widths = [1, 1, 1, 1, 1, 1];
        int[] groups = [0, 0, 1, 1, 2, 2];
        string[] kinds = ["G", "G", "M", "M", "G", "G"];
        bool[] stacked = [true, true, false, false, true, true];
        var rows = GridRows.Layout(widths, groups, kinds, stacked, 6);
        Assert.Equal(["G", "G", "M", "M", "G", "G"], Assert.Single(rows).Select(i => kinds[i]));
    }

    [Fact]
    public void A_grid_wider_than_a_row_gets_its_own()
    {
        // the row of the torn pouch reads symmetric, the row of the two single ones follows it
        Assert.Equal("8 | 2+1", Widths(6, P("a", 2), P("b", 8), P("c", 1)));
    }

    /// <summary>
    /// A kind the rig carries only once cannot be symmetric: such pouches share a row, widest at
    /// the centre, and that row comes after the rows that read symmetric.
    /// </summary>
    [Fact]
    public void Pouches_the_rig_carries_once_share_a_row_after_the_rest()
    {
        // one medical (2x2), one admin (2x1) and one utility (2x2 + 2x1): one row, not three
        Assert.Equal("U D A", Layout(6, P("D", 2), P("A", 2), P("U", 2)));

        // the widest at the centre, the next one beside it
        Assert.Equal("A2 M A", Layout(6, P("M", 2), P("A", 1), P("A2", 1)));

        // a single pouch in the middle of four of a kind is symmetric and stays in that row;
        // the two that are left share a row of their own, after it
        Assert.Equal("G G A G G | M D", Layout(6, [.. Many(4, "G", 1), P("M", 2), P("D", 2), P("A", 2)]));
    }
}