using System.Text.Json;
using ModularVests.Server.Config;
using ModularVests.Server.Services;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// The recoloured rig bundles: every rig that names one has it in the manifest the mod ships,
/// and the carriers they are built from are the recolour kits in assets/vests.
/// </summary>
public class VestBundlesTests
{
    private static ItemsConfig Shipped() => ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));

    [Fact]
    public void Every_rig_bundle_is_in_the_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(TestPaths.BundleManifest));
        var keys = manifest.RootElement.GetProperty("manifest")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("key").GetString())
            .ToHashSet(StringComparer.Ordinal);

        var missing = Shipped().AllVests()
            .Where(vest => vest.Prefab.Length > 0 && !keys.Contains(vest.Prefab))
            .Select(vest => $"{vest.Key} -> {vest.Prefab}")
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A rig bundle is a clone of its donor's, so it needs the donor's bundle loaded first;
    /// without that dependency the meshes and the maps it left behind come back as null.
    /// </summary>
    [Fact]
    public void Every_rig_bundle_depends_on_something()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(TestPaths.BundleManifest));
        var entries = manifest.RootElement.GetProperty("manifest").EnumerateArray()
            .Where(entry => entry.GetProperty("key").GetString()!.StartsWith("modularvests/vests/", StringComparison.Ordinal))
            .ToList();

        Assert.All(entries, entry =>
            Assert.NotEmpty(entry.GetProperty("dependencyKeys").EnumerateArray()));
    }

    /// <summary>
    /// The colours of one carrier share what a single item's weight would be: five colours at
    /// the prototype's full share would put five times as many modular rigs on bots and in loot.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Colours_of_a_carrier_split_the_prototype_weight(int sharing)
    {
        // a prototype holding a fifth of a bot's armor vests, in a pool of 500 rigs
        var single = BotPoolRegistrar.Weight(20, 100, 500, 1, 1);
        var each = BotPoolRegistrar.Weight(20, 100, 500, 1, sharing);

        Assert.Equal(100, single);
        Assert.Equal(single / sharing, each);
        Assert.Equal(single, each * sharing);
    }

    /// <summary>A weight that would round away still puts the rig on a bot now and then.</summary>
    [Fact]
    public void A_rare_prototype_still_reaches_a_bot()
    {
        Assert.Equal(1, BotPoolRegistrar.Weight(1, 1000, 100, 1, 5));
    }
}
