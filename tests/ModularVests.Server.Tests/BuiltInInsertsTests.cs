using ModularVests.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace ModularVests.Server.Tests;

public class BuiltInInsertsTests
{
    private static readonly MongoId Plate = new("656f57dc27aed95beb08f628");
    private static readonly MongoId First = new("656fa0fb498d1b7e3e071d9c");
    private static readonly MongoId Second = new("656fa25e94b480b8a500c0e0");

    private static Slot MakeSlot(string name, bool required, MongoId? plate, params MongoId[] filter) => new()
    {
        Name = name,
        Required = required,
        Properties = new SlotProperties
        {
            Filters = [new SlotFilter { Plate = plate, Filter = filter.ToHashSet() }],
        },
    };

    private static TemplateItem Rig(params Slot[] slots) => new()
    {
        Id = new MongoId("68948a95d8f2b85fb705e2a6"),
        Properties = new TemplateItemProperties { Slots = slots.ToList() },
    };

    [Fact]
    public void A_required_slot_gets_its_default_plate()
    {
        var result = BuiltInInserts.For(Rig(MakeSlot("Soft_armor_front", true, Plate, First, Plate)));
        Assert.Equal([("Soft_armor_front", Plate)], result.Inserts);
        Assert.Empty(result.MissingSlots);
    }

    [Fact]
    public void Without_a_default_plate_the_first_accepted_part_is_taken()
    {
        var result = BuiltInInserts.For(Rig(MakeSlot("Collar", true, null, First, Second)));
        Assert.Equal([("Collar", First)], result.Inserts);
    }

    [Fact]
    public void A_required_slot_with_no_candidate_is_reported()
    {
        var result = BuiltInInserts.For(Rig(MakeSlot("Collar", true, null)));
        Assert.Empty(result.Inserts);
        Assert.Equal(["Collar"], result.MissingSlots);
    }

    /// <summary>The default preset carries the default plates too; pouch cells, which name none, stay empty.</summary>
    [Fact]
    public void With_plates_the_default_plates_come_along()
    {
        var result = BuiltInInserts.For(Rig(
            MakeSlot("Front_plate", false, Plate, Plate),
            MakeSlot("mod_pouch_1", false, null, First),
            MakeSlot("Soft_armor_back", true, Second, Second)), withPlates: true);
        Assert.Equal([("Front_plate", Plate), ("Soft_armor_back", Second)], result.Inserts);
        Assert.Empty(result.MissingSlots);
    }

    /// <summary>Plates and pouch cells are optional: the trader row carries neither.</summary>
    [Fact]
    public void Optional_slots_are_ignored()
    {
        var result = BuiltInInserts.For(Rig(
            MakeSlot("Front_plate", false, Plate, Plate),
            MakeSlot("mod_pouch_1", false, null, First),
            MakeSlot("Soft_armor_back", true, Second, Second)));
        Assert.Equal([("Soft_armor_back", Second)], result.Inserts);
        Assert.Empty(result.MissingSlots);
    }

    [Fact]
    public void A_template_without_slots_has_no_inserts()
    {
        var result = BuiltInInserts.For(new TemplateItem { Id = Plate });
        Assert.Empty(result.Inserts);
        Assert.Empty(result.MissingSlots);
    }

    /// <summary>
    /// The trader has to buy the parts as well as the rig. A trader may only buy an item when
    /// it buys everything inside it too, and a rig always carries its panels: without this the
    /// rigs he sells cannot be sold back to him.
    /// </summary>
    [Fact]
    public void Everything_a_rigs_slots_accept_is_listed_for_the_trader()
    {
        var inserts = ModularItemsRegistrar.InsertsOf(Rig(
            MakeSlot("Soft_armor_front", true, null, First, Second),
            MakeSlot("Front_plate", false, Plate, Plate),
            MakeSlot("mod_pouch_1", false, null, First))).ToList();

        // every panel and every plate the rig accepts, each once
        Assert.Equal([First, Second, Plate], inserts);
    }

    /// <summary>A slot's default plate counts even when the filter does not name it.</summary>
    [Fact]
    public void A_default_plate_outside_the_filter_is_listed_too()
    {
        var inserts = ModularItemsRegistrar.InsertsOf(Rig(
            MakeSlot("Front_plate", false, Plate))).ToList();
        Assert.Equal([Plate], inserts);
    }

    [Fact]
    public void A_template_without_slots_offers_the_trader_nothing()
    {
        Assert.Empty(ModularItemsRegistrar.InsertsOf(new TemplateItem { Id = Plate }));
    }
}
