using ModularVests.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Services;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// A lost insured rig comes back with every pouch it carried, from the rig's trader, and the
/// trader never keeps a pouch back.
/// </summary>
public class InsuredPouchesTests
{
    private static readonly MongoId Session = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly MongoId Prapor = new("54cb50c76803fa8b248b4571");
    private static readonly MongoId Therapist = new("54cb57776803fa99248b456e");
    private static readonly MongoId Tpl = new("cccccccccccccccccccccccc");

    private static Item Item(string id, string? parent = null, string? slot = null) =>
        new() { Id = new MongoId(id), Template = Tpl, ParentId = parent, SlotId = slot };

    private static InsuranceEquipmentPkg Package(Item item, MongoId trader) =>
        new() { SessionId = Session, ItemToReturnToPlayer = item, TraderId = trader };

    private const string Rig = "111111111111111111111111";
    private const string OtherRig = "222222222222222222222222";

    [Fact]
    public void An_uninsured_pouch_comes_back_with_its_rig()
    {
        var packages = new List<InsuranceEquipmentPkg> { Package(Item(Rig, "equipment", "TacticalVest"), Prapor) };
        var pouch = Item("333333333333333333333333", Rig, "mod_pouch_1");

        Assert.Equal(1, InsuredPouches.Complete(packages, [pouch], Session, null));

        var added = packages.Single(p => p.ItemToReturnToPlayer!.Id == pouch.Id);
        Assert.Equal(Prapor, added.TraderId);
        Assert.Equal(Rig, added.ItemToReturnToPlayer!.ParentId);
        Assert.Equal("mod_pouch_1", added.ItemToReturnToPlayer.SlotId);
    }

    /// <summary>Insured with somebody else, it would come home in another package, on its own.</summary>
    [Fact]
    public void An_insured_pouch_follows_its_rig_to_the_rig_s_trader()
    {
        var pouch = Item("333333333333333333333333", Rig, "mod_pouch_5");
        var packages = new List<InsuranceEquipmentPkg>
        {
            Package(Item(Rig, "equipment", "TacticalVest"), Prapor),
            Package(pouch, Therapist),
        };

        Assert.Equal(0, InsuredPouches.Complete(packages, [pouch], Session, null));
        Assert.Equal(2, packages.Count);
        Assert.All(packages, p => Assert.Equal(Prapor, p.TraderId));
    }

    [Fact]
    public void Pouches_of_a_rig_that_was_not_lost_stay_where_they_are()
    {
        var packages = new List<InsuranceEquipmentPkg> { Package(Item(Rig, "equipment", "TacticalVest"), Prapor) };
        var elsewhere = Item("333333333333333333333333", OtherRig, "mod_pouch_1");

        Assert.Equal(0, InsuredPouches.Complete(packages, [elsewhere], Session, null));
        Assert.Single(packages);
    }

    [Fact]
    public void The_trader_keeps_no_pouch_back()
    {
        var attachments = new Dictionary<MongoId, List<Item>>
        {
            [new MongoId(Rig)] =
            [
                Item("333333333333333333333333", Rig, "mod_pouch_1"),
                Item("444444444444444444444444", Rig, "Front_plate"),
            ],
            [new MongoId(OtherRig)] = [Item("555555555555555555555555", OtherRig, "mod_pouch_2")],
        };

        var left = InsuredPouches.WithoutPouches(attachments);

        Assert.Equal("Front_plate", Assert.Single(Assert.Single(left).Value).SlotId);
    }
}
