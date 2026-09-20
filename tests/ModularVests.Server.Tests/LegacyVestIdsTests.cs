using ModularVests.Server.Config;
using ModularVests.Server.Services;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// The eighteen rig keys that shipped before the line-up was rebuilt around the mod's own five
/// colours. Every one of them is in the profiles of the players who already have the mod, so the
/// key - and with it the template id, the cell ids and the preset id - must survive the rebuild:
/// a recoloured rig is the same item with a different look, never a new one.
///
/// Never "fix" this test by updating a constant. A failure means either a key was renamed or
/// dropped (profiles lose the rig and every pouch inside it) or the id algorithm moved.
/// </summary>
public class LegacyVestIdsTests
{
    /// <summary>key, template id, id of cell mod_pouch_1, id of the default preset.</summary>
    [Theory]
    [InlineData("6b45", "dd9d6dc7bfb753bc5da72eb8", "338d2d16174093edcab023a6", "f0abf51391ed41facca0d7c0")]
    [InlineData("iotv_fp", "82832f28716e8cb832ace4fb", "6622ed2a8e162734a98a023e", "f89a2a37005294184d29f60b")]
    [InlineData("iotv_assault", "70db1566d7d48440a5f9b107", "b71339b18147269f7a1e7df0", "8427464ab31297eeb5c1772a")]
    [InlineData("iotv_hm", "d8a3220d96856b0e53fa7136", "fc56a816099964c7236d9b43", "63936df18c26a4f821684f5a")]
    [InlineData("gladiator_s", "6f0827a8b2c03782e2b44e32", "e005e1be51c2f56a545597e5", "be6cc42b8827a342907c049f")]
    [InlineData("6b43", "ed8eb7e6c8457ef0d40a9231", "cff37e110fb803d730647af8", "049c6c15413841e5a69f7ff3")]
    [InlineData("trooper_multicam", "7e7bfccf6330092fc21ce096", "89268de209bae10ad8fb518d", "218fed4f97e63087caba7938")]
    [InlineData("trooper_coyote", "da1fd86d9c27f66a142992f7", "c8e77f485c90c36fa3742db3", "d66482019cd16596d0a5a74f")]
    [InlineData("otv_ucp", "82a61a933a5e422b298ade2a", "8c908a233d2c1d5955f57fc3", "5b183f871c6cd25ab7e61306")]
    [InlineData("otv_woodland", "71d99df9f39fa40ac83b70bd", "8da517e7f6a14785e097565a", "a597b64d787bd90827137645")]
    [InlineData("otv_cce", "34f302933a3e849d5d05f15f", "4f65e339848f89a833ad1451", "787037795a329142aa923506")]
    [InlineData("otv_3c", "7844dfbdc291cd67023c87a1", "84cfde0fb67c09cc24b26915", "e06dfe4e1f277b2f94412d7c")]
    [InlineData("untar", "e0dfd88cf5531b25ac3fa283", "7514ba481bb4814678818927", "5d08f834d796704bec72218f")]
    [InlineData("untar_dbdu", "e206381dbc08ba6b64a47bc0", "4d8d5e84460d58e1784d5d04", "7383e110aec8fd4518ccc3f3")]
    [InlineData("untar_marpat", "5bedbe72a6f5faab48db8bb6", "142423ef0f875d8f6752cf80", "b5d8e5665571f455b1672c6c")]
    [InlineData("untar_wineleaf", "9148ba816a9a92c1cc318dad", "c8381e62b8757f9ada20198c", "d8747cdb2c7ba552c9cffae8")]
    [InlineData("thor", "27c664f95226155640d55aed", "7303e3c5b14ca907654da41a", "bb0c4871ccf92a9b8102e3f3")]
    [InlineData("thor_masgray", "b08ba50c46cd9c4c789c0910", "9f6bbd158fb8492b5ad1752b", "4414920da81abe8394f6ef70")]
    public void Legacy_vest_ids_are_frozen(string key, string vestId, string firstCellId, string presetId)
    {
        Assert.Equal(vestId, DeterministicId.For(DeterministicId.VestKey(key)));
        Assert.Equal(firstCellId, DeterministicId.For(DeterministicId.VestSlotKey(key, ClusterGrid.SlotName(1))));
        Assert.Equal(presetId, DeterministicId.For(DeterministicId.PresetKey(key)));
    }

    /// <summary>
    /// Every legacy key is still a rig of the line-up - now as the colour that took it over. A
    /// key that went missing here is a rig that vanished from every profile that holds one.
    /// </summary>
    [Fact]
    public void Every_legacy_key_is_still_shipped()
    {
        var config = ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));
        var shipped = config.AllVests().Select(v => v.Key).ToHashSet(StringComparer.Ordinal);

        var missing = LegacyKeys.Where(k => !shipped.Contains(k)).ToList();
        Assert.Empty(missing);
    }

    /// <summary>No two rigs may claim the same key: one key is one item in a profile.</summary>
    [Fact]
    public void Shipped_vest_keys_are_unique()
    {
        var keys = ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig)).AllVests().Select(v => v.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    internal static readonly string[] LegacyKeys =
    [
        "6b45", "iotv_fp", "iotv_assault", "iotv_hm", "gladiator_s", "6b43",
        "trooper_multicam", "trooper_coyote", "otv_ucp", "otv_woodland", "otv_cce", "otv_3c",
        "untar", "untar_dbdu", "untar_marpat", "untar_wineleaf", "thor", "thor_masgray",
    ];
}
