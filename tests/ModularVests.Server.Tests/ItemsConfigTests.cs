using System.Text.Json;
using System.Text.Json.Nodes;
using ModularVests.Server.Config;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace ModularVests.Server.Tests;

public class ItemsConfigTests
{
    private static ItemsConfig Shipped() => ItemsConfig.Parse(File.ReadAllText(TestPaths.ItemsConfig));

    [Fact]
    public void Shipped_config_is_valid()
    {
        var config = Shipped();
        Assert.Empty(config.Validate());

        // ten carriers, five colours each
        Assert.Equal(["6b45", "iotv_fp", "iotv_assault", "iotv_hm", "gladiator_s", "6b43",
                "trooper", "otv", "untar", "thor"],
            config.Vests.Select(v => v.Key));
        Assert.Equal(50, config.AllVests().Count);
        Assert.All(config.Vests, v => Assert.Equal(5, v.Variants.Count));

        // no cummerbund: the chest clusters only
        Assert.All(config.AllVests(), v => Assert.Equal(
            v.Model is "trooper" or "otv" ? 2 : 4, v.Clusters));

        // every rig is off a donor that ships with the game or with WTT: nothing optional left
        Assert.Empty(config.AllVests().Where(v => v.Optional));
        Assert.Empty(config.AllVests().Where(v => v.TierFrom.Length > 0));
        Assert.Empty(config.Warnings());
        Assert.Equal(13, config.Pouches.Count);
        Assert.Equal(65, config.AllPouches().Count);
        Assert.Equal(4, config.Vests[0].Clusters);
    }

    /// <summary>
    /// Every key the mod shipped is still a rig, now as the colour that took it over; the rest of
    /// the line-up is keyed carrier_colour. Keys live in profiles: see LegacyVestIdsTests.
    /// </summary>
    [Fact]
    public void Legacy_keys_are_the_colours_that_took_them_over()
    {
        var config = Shipped();
        var taken = config.Vests
            .SelectMany(v => v.Variants.Where(variant => variant.ItemKey.Length > 0)
                .Select(variant => (Vest: v.Key, variant.Key, variant.ItemKey)))
            .ToList();

        Assert.Equal(LegacyVestIdsTests.LegacyKeys.Order(), taken.Select(t => t.ItemKey).Order());
        Assert.Equal("black", taken.Single(t => t.ItemKey == "otv_ucp").Key);
        Assert.Equal("olive", taken.Single(t => t.ItemKey == "gladiator_s").Key);
        Assert.Equal("black", taken.Single(t => t.ItemKey == "untar").Key);

        // a rig with no key of its own is keyed after its carrier and colour
        Assert.Contains("otv_multicam", config.AllVests().Select(v => v.Key));
        Assert.Contains("thor_emr_summer", config.AllVests().Select(v => v.Key));
    }

    /// <summary>A rig's bundle and its recolour kit follow from the carrier and the colour.</summary>
    [Fact]
    public void Every_rig_names_the_bundle_of_its_carrier_and_colour()
    {
        var kits = new HashSet<string> { "6b45", "iotv", "gladiator_s", "6b43", "trooper", "otv", "untar", "thor" };
        foreach (var vest in Shipped().AllVests())
        {
            Assert.Equal($"modularvests/vests/{vest.Carrier}_{vest.Color}.bundle", vest.Prefab);
            Assert.Equal("", vest.Rcid); // the asset inside a clone is named like the bundle
            Assert.Contains(vest.Model, kits);
            Assert.Equal(vest.Color, vest.PouchColor);
        }
    }

    /// <summary>Each cell accepts every pouch whose footprint fits from it.</summary>
    [Fact]
    public void Pouch_cells_have_a_name_in_every_language()
    {
        var config = Shipped();
        // upper case like the game's own cell labels (MOD_MAGAZINE reads "MAGAZINE")
        Assert.Equal("ПОДСУМОК", config.PouchSlotNameFor("ru"));
        Assert.Equal("POUCH", config.PouchSlotNameFor("en"));
        Assert.Equal("ポーチ", config.PouchSlotNameFor("jp"));
        Assert.Equal("POUCH", config.PouchSlotNameFor("hu")); // no text of its own: English

        config.PouchSlotName = new Dictionary<string, string> { ["ru"] = "ПОДСУМОК" };
        Assert.Contains(config.Validate(), e => e.Contains("pouchSlotName"));
    }

    [Fact]
    public void Shipped_cells_accept_what_fits()
    {
        var config = Shipped();
        string[] Models(int position) => config.PouchesAt(position)
            .Select(p => p.Key.Split('_')[0]).Distinct().Order().ToArray();

        Assert.Equal(["admin03", "bottlepouch", "fraggrenade", "gadget01", "grenadepouch", "magpouch02", "magpouch03",
            "magpouch07", "medpouch", "smallpouch", "survival06", "utilitypouch", "verticalpouch"], Models(1));
        Assert.Equal(["bottlepouch", "fraggrenade", "gadget01", "grenadepouch", "magpouch02", "magpouch03", "magpouch07",
            "smallpouch", "verticalpouch"], Models(2));
        Assert.Equal(["admin03", "fraggrenade", "grenadepouch", "smallpouch"], Models(3));
        Assert.Equal(["fraggrenade", "grenadepouch", "smallpouch"], Models(4));
    }

    [Fact]
    public void Variants_expand_into_pouches_of_their_own()
    {
        var mags = Shipped().AllPouches().Where(p => p.Key.StartsWith("magpouch07_")).ToList();
        Assert.Equal(["magpouch07_coyote", "magpouch07_olive", "magpouch07_multicam", "magpouch07_black", "magpouch07_emr_summer"],
            mags.Select(p => p.Key));

        var black = mags[3];
        Assert.Equal("modularvests/magpouch_07_black.bundle", black.Prefab);
        Assert.Equal("magpouch_07_black", black.Rcid);
        Assert.Equal("modularvests_pouch_magpouch07_black", black.Name);
        Assert.Equal("Подсумок магазинный сдвоенный (Черный)", black.Locales["ru"].Name);
        Assert.Equal("Double magazine pouch (Black)", black.Locales["en"].Name);
        Assert.Equal(["main", "second"], black.Grids.Select(g => g.Name));
        Assert.Empty(black.Variants);

        var openTop = Shipped().AllPouches().Where(p => p.Key.StartsWith("magpouch02_")).ToList();
        Assert.Equal(["magpouch02_coyote", "magpouch02_olive", "magpouch02_multicam", "magpouch02_black", "magpouch02_emr_summer"],
            openTop.Select(p => p.Key));
        Assert.All(openTop, p => Assert.Equal(["main"], p.Grids.Select(g => g.Name)));
        Assert.Equal("modularvests/magpouch_02_opentop_olive.bundle", openTop[1].Prefab);
        Assert.Equal("Подсумок магазинный открытый (Олива)", openTop[1].Locales["ru"].Name);
        Assert.Equal("Подсумок магазинный (Черный)",
            Shipped().AllPouches().Single(p => p.Key == "magpouch03_black").Locales["ru"].Name);

        var flapped = Shipped().AllPouches().Where(p => p.Key.StartsWith("magpouch03_")).ToList();
        Assert.Equal(["magpouch03_coyote", "magpouch03_olive", "magpouch03_multicam", "magpouch03_black", "magpouch03_emr_summer"],
            flapped.Select(p => p.Key));
        Assert.Equal("magpouch_03_flapper_close_black", flapped[3].Rcid);

        var frag = Shipped().AllPouches().Where(p => p.Key.StartsWith("fraggrenade_")).ToList();
        Assert.Equal(["fraggrenade_multicam", "fraggrenade_black", "fraggrenade_coyote", "fraggrenade_olive", "fraggrenade_emr_summer"],
            frag.Select(p => p.Key));
        Assert.Equal("modularvests/frag_grenade_pouch_multicam.bundle", frag[0].Prefab);
        Assert.Equal("frag_grenade_pouch_multicam", frag[0].Rcid);
        Assert.Equal("Подсумок гранатный Warrior (MultiCam)", frag[0].Locales["ru"].Name);
        Assert.Equal("1x1", frag[0].Footprint);
        Assert.Equal((1, 1), (frag[0].Width, frag[0].Height));
        Assert.Equal((1, 1), (Assert.Single(frag[0].Grids).Width, frag[0].Grids[0].Height));
    }

    /// <summary>
    /// The colour names and the two descriptions are written once and reach every item: with
    /// eleven languages, a copy per item would be a thousand strings where seventy-seven do.
    /// </summary>
    [Fact]
    public void Shared_blocks_reach_every_item()
    {
        var config = Shipped();
        var languages = new[] { "en", "ru", "ch", "es", "po", "ge", "fr", "jp", "pl", "tu", "kr" };

        Assert.Equal(["coyote", "olive", "multicam", "black", "emr_summer"], config.Colors.Keys);
        Assert.All(config.Colors.Values, c => Assert.Equal(languages, c.Keys));
        Assert.Equal(languages, config.VestDescription.Keys);
        Assert.Equal(languages, config.PouchDescription.Keys);

        // every item carries every language, and no locale entry is left without a description
        foreach (var locales in config.Vests.Select(v => v.Locales))
        {
            Assert.Equal(languages, locales.Keys);
            Assert.All(locales.Values, t => Assert.NotEmpty(t.Description));
        }

        foreach (var pouch in config.AllPouches())
        {
            Assert.Equal(languages, pouch.Locales.Keys);
            Assert.All(pouch.Locales.Values, t => Assert.NotEmpty(t.Description));
        }

        Assert.Equal(config.VestDescription["ru"], config.Vests[0].Locales["ru"].Description);
        Assert.Equal(config.PouchDescription["jp"],
            config.AllPouches()[0].Locales["jp"].Description);

        // a colour name that is neither the variant's own nor in "colors" is an error, not a key
        config.Pouches[0].Variants[0].Colors.Clear();
        Assert.Contains(config.Validate(), e => e.Contains("has no colour text"));
    }

    /// <summary>An item that writes its own description keeps it.</summary>
    [Fact]
    public void An_own_description_wins_over_the_shared_one()
    {
        var config = ItemsConfig.Parse("""
            {
              "pouchDescription": { "en": "shared", "ru": "общее" },
              "vests": [],
              "pouches": [
                { "key": "own", "locales": { "en": { "description": "mine" }, "ru": {} } }
              ]
            }
            """);
        var locales = config.Pouches[0].Locales;
        Assert.Equal("mine", locales["en"].Description);
        Assert.Equal("общее", locales["ru"].Description);
    }

    /// <summary>A language the shared block does not cover falls back to English.</summary>
    [Fact]
    public void A_shared_description_falls_back_to_english()
    {
        var config = ItemsConfig.Parse("""
            {
              "vestDescription": { "en": "shared" },
              "vests": [{ "key": "v", "locales": { "kr": {} } }],
              "pouches": []
            }
            """);
        Assert.Equal("shared", config.Vests[0].Locales["kr"].Description);
    }

    /// <summary>
    /// A colour key is part of the item id: every pouch uses the same five common colour names,
    /// so one colour never goes by two keys.
    /// </summary>
    [Fact]
    public void Colour_keys_come_from_one_set()
    {
        string[] colours = ["coyote", "olive", "multicam", "black", "emr_summer"];
        var config = Shipped();
        foreach (var pouch in config.Pouches)
        {
            Assert.All(pouch.Variants, v => Assert.Contains(v.Key, colours));
        }

        Assert.Equal(colours, config.Colours());

        // the rigs come in the same five, and every carrier offers all of them
        foreach (var vest in config.Vests)
        {
            Assert.Equal(colours.Order(), vest.Variants.Select(v => v.Key).Order());
        }

        // a rig's matched pouch colour is one of the same five, or nothing at all
        Assert.All(config.AllVests().Where(v => v.PouchColor.Length > 0),
            v => Assert.Contains(v.PouchColor, colours));

        config.Vests[0].Variants[0].Key = "teal";
        Assert.Contains(config.Validate(), e => e.Contains("variant 'teal'"));
    }

    /// <summary>
    /// Which pouches a bot fills with rifle magazines, and the rig colours they are matched to.
    /// The table is the author's (2026-09-20); a rig without an entry gets a random colour.
    /// </summary>
    [Fact]
    public void Bot_fields_are_the_authors_table()
    {
        var config = Shipped();

        Assert.Equal(new Dictionary<string, int>
            {
                ["magpouch07"] = 2,
                ["magpouch02"] = 1,
                ["magpouch03"] = 1,
            },
            config.Pouches.Where(p => p.IsRifleMagPouch).ToDictionary(p => p.Key, p => p.MagSlots));
        Assert.All(config.Pouches.Where(p => !p.IsRifleMagPouch), p => Assert.Equal(0, p.MagSlots));

        // a rig's pouches are matched to the colour it wears; every rig has one now
        Assert.Empty(config.AllVests().Where(v => v.PouchColor.Length == 0));
        Assert.Equal(new Dictionary<string, string>
            {
                ["6b45"] = "emr_summer",
                ["6b43"] = "emr_summer",
                ["iotv_fp"] = "multicam",
                ["iotv_assault"] = "multicam",
                ["iotv_hm"] = "multicam",
                ["trooper_multicam"] = "multicam",
                ["gladiator_s"] = "olive",
                ["otv_woodland"] = "olive",
                ["otv_cce"] = "emr_summer",
                ["otv_ucp"] = "black",
                ["otv_3c"] = "coyote",
                ["untar"] = "black",
                ["untar_marpat"] = "emr_summer",
                ["untar_wineleaf"] = "olive",
                ["untar_dbdu"] = "coyote",
                ["thor"] = "coyote",
                ["thor_masgray"] = "black",
                ["trooper_coyote"] = "coyote",
            },
            config.AllVests().Where(v => LegacyVestIdsTests.LegacyKeys.Contains(v.Key))
                .ToDictionary(v => v.Key, v => v.PouchColor));
    }

    /// <summary>The gadget pouch grew to two cells and two sections; its first grid keeps its name.</summary>
    [Fact]
    public void Gadget_pouch_is_two_cells_with_two_sections()
    {
        var gadget = Shipped().AllPouches().Single(p => p.Key == "gadget01_black");
        Assert.Equal("1x2", gadget.Footprint);
        Assert.Equal(["main", "second"], gadget.Grids.Select(g => g.Name));
        Assert.All(gadget.Grids, g => Assert.Equal((1, 1), (g.Width, g.Height)));
        Assert.Equal("Подсумок для гаджетов (Черный)", gadget.Locales["ru"].Name);
    }

    /// <summary>The pouches of the line-up: sizes on the rig, sections and names.</summary>
    [Theory]
    [InlineData("smallpouch", "1x1", "main:1x1", "Подсумок пистолетный магазинный (Coyote)")]
    [InlineData("grenadepouch", "1x1", "main:1x1", "Подсумок гранатный (Coyote)")]
    [InlineData("verticalpouch", "1x2", "main:1x2 second:1x2", "Подсумок утилитарный узкий (Coyote)")]
    [InlineData("bottlepouch", "1x2", "main:1x2 second:1x2", "Подсумок бутылочный (Coyote)")]
    [InlineData("medpouch", "2x2", "main:2x2", "Подсумок медицинский (Coyote)")]
    [InlineData("utilitypouch", "2x2", "main:2x2 second:2x1", "Подсумок утилитарный (Coyote)")]
    // not "large": the same 2x2 and the same six cells as the utility pouch
    [InlineData("survival06", "2x2", "main:2x3", "Подсумок грузовой (Coyote)")]
    [InlineData("fraggrenade", "1x1", "main:1x1", "Подсумок гранатный Warrior (Coyote)")]
    public void Line_up_pouch(string key, string footprint, string grids, string russianName)
    {
        var pouch = Shipped().AllPouches().Single(p => p.Key == key + "_coyote");
        Assert.Equal(footprint, pouch.Footprint);
        Assert.Equal(grids, string.Join(" ", pouch.Grids.Select(g => $"{g.Name}:{g.Width}x{g.Height}")));
        Assert.Equal(russianName, pouch.Locales["ru"].Name);
        Assert.Equal(footprint, $"{pouch.Width}x{pouch.Height}");
    }

    [Fact]
    public void Validation_rejects_bad_grids()
    {
        var config = Shipped();
        config.Pouches[0].Grids = [];
        Assert.Contains(config.Validate(), e => e.Contains("no grids"));

        config.Pouches[0].Grids = [new GridConfig { Name = "main", Width = 1, Height = 1 }, new GridConfig { Name = "main", Width = 0, Height = 1 }];
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("duplicated"));
        Assert.Contains(errors, e => e.Contains("at least 1x1"));
    }

    [Fact]
    public void A_smaller_footprint_is_accepted_wherever_it_fits()
    {
        var config = Shipped();
        var med = config.Pouches.Single(p => p.Key == "medpouch");
        med.Footprint = "1x2";
        Assert.Equal([1, 2], Enumerable.Range(1, 4).Where(p => config.PouchesAt(p).Any(x => x.Key == "medpouch_coyote")));
        med.Footprint = "1x1";
        Assert.All(Enumerable.Range(1, 4), p => Assert.Contains(config.PouchesAt(p), x => x.Key == "medpouch_coyote"));
    }

    [Fact]
    public void Validation_rejects_an_unknown_footprint()
    {
        var config = Shipped();
        config.Pouches[0].Footprint = "3x1";
        Assert.Contains(config.Validate(), e => e.Contains("footprint '3x1'"));
        config.Pouches[0].Footprint = "";
        Assert.Contains(config.Validate(), e => e.Contains("footprint ''"));
    }

    [Fact]
    public void Loyalty_level_defaults_to_one_and_reaches_the_variants()
    {
        var config = Shipped();
        Assert.All(config.AllPouches(), p => Assert.Equal(1, p.LoyaltyLevel));
        Assert.All(config.Vests, v => Assert.Equal(1, v.LoyaltyLevel));

        config.Pouches.Single(p => p.Key == "magpouch07").LoyaltyLevel = 3;
        Assert.All(config.AllPouches().Where(p => p.Key.StartsWith("magpouch07_")), p => Assert.Equal(3, p.LoyaltyLevel));
    }

    [Fact]
    public void Validation_rejects_a_loyalty_level_out_of_range()
    {
        var config = Shipped();
        config.Pouches[0].LoyaltyLevel = 5;
        config.Vests[0].LoyaltyLevel = 0;
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("pouch 'magpouch07_") && e.Contains("loyaltyLevel"));
        Assert.Contains(errors, e => e.Contains("vest '6b45': loyaltyLevel"));
    }

    /// <summary>The line-up moved to the mod's own trader; a file that still names one is read as before.</summary>
    [Fact]
    public void A_config_with_the_old_trader_id_still_parses()
    {
        var config = ItemsConfig.Parse("""{ "traderId": "5ac3b934156ae10c4430e83c", "vests": [], "pouches": [] }""");
        Assert.Empty(config.Vests);
    }

    [Fact]
    public void Validation_rejects_a_rig_without_clusters()
    {
        var config = Shipped();
        config.Vests[0].Clusters = 0;
        Assert.Contains(config.Validate(), e => e.Contains("clusters must be at least 1"));
    }

    [Fact]
    public void An_odd_cluster_count_is_a_warning_not_an_error()
    {
        var config = Shipped();
        config.Vests[0].Clusters = 3;
        Assert.Empty(config.Validate());
        Assert.Contains(config.Warnings(), w => w.Contains("no mirrored pair"));
    }

    [Fact]
    public void Roundtrip_keeps_every_field()
    {
        var first = Shipped().Serialize();
        var second = ItemsConfig.Parse(first).Serialize();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(first), JsonNode.Parse(second)), second);

        // and nothing the file says is dropped on the way in
        var source = JsonNode.Parse(File.ReadAllText(TestPaths.ItemsConfig),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        AssertContained(source, JsonNode.Parse(first)!, "$");
    }

    private static void AssertContained(JsonNode expected, JsonNode actual, string path)
    {
        switch (expected)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var child = actual.AsObject()[key];
                    Assert.True(child != null || value == null, $"{path}.{key} was lost");
                    if (value != null)
                    {
                        AssertContained(value, child!, $"{path}.{key}");
                    }
                }

                break;
            case JsonArray arr:
                Assert.Equal(arr.Count, actual.AsArray().Count);
                for (var i = 0; i < arr.Count; i++)
                {
                    AssertContained(arr[i]!, actual.AsArray()[i]!, $"{path}[{i}]");
                }

                break;
            default:
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{path}: {expected} != {actual}");
                break;
        }
    }

    [Fact]
    public void Overrides_map_onto_template_properties()
    {
        var props = Shipped().Vests[0].Overrides.Deserialize<TemplateItemProperties>()!;
        Assert.Equal("", props.RigLayoutName);
        Assert.True(props.BlocksArmorVest);
        Assert.Equal(3, props.Width);
        Assert.Equal(4, props.Height);
        Assert.Equal(3.65, props.Weight);
        Assert.Null(props.Slots);
        Assert.Null(props.Grids);
    }

    [Fact]
    public void Validation_rejects_mod_built_properties_in_overrides()
    {
        var config = Shipped();
        config.Vests[0].Overrides["Slots"] = new JsonArray();
        Assert.Contains(config.Validate(), e => e.Contains("Slots"));
    }
}
