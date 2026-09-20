using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ModularVests.Server.Services;

/// <summary>
/// The parts a rig is built with: its required slots (soft armor panels, collar) and the
/// default part of each. A trader sells the item assembled (vanilla armored rigs carry exactly
/// such child rows) and its default preset lists the same parts - without them the rig arrives
/// with empty vital slots: red, "missing module", and with none of its own armor.
/// One source for both, so the two cannot drift apart. The default preset also carries the
/// default plates (<c>withPlates</c>), as vanilla armor presets do: Fence wears them down at
/// random and warned of every plate slot it found empty; the trader keeps selling without them.
/// </summary>
public static class BuiltInInserts
{
    public sealed record Result(List<(string Slot, MongoId Tpl)> Inserts, List<string> MissingSlots);

    /// <summary>
    /// Required slots: the filter's default plate, else the first template it accepts. A required
    /// slot with neither comes back in <see cref="Result.MissingSlots"/>. With
    /// <paramref name="withPlates"/>, also every other slot that names a default plate.
    /// </summary>
    public static Result For(TemplateItem template, bool withPlates = false)
    {
        var inserts = new List<(string, MongoId)>();
        var missing = new List<string>();

        foreach (var slot in template.Properties?.Slots ?? [])
        {
            if (slot.Name == null)
            {
                continue;
            }

            var filter = slot.Properties?.Filters?.FirstOrDefault();
            if (slot.Required != true)
            {
                var plate = filter?.Plate ?? default;
                if (withPlates && !plate.IsEmpty)
                {
                    inserts.Add((slot.Name, plate));
                }

                continue;
            }

            var insert = filter?.Plate ?? default;
            if (insert.IsEmpty)
            {
                insert = filter?.Filter?.FirstOrDefault() ?? default;
            }

            if (insert.IsEmpty)
            {
                missing.Add(slot.Name);
                continue;
            }

            inserts.Add((slot.Name, insert));
        }

        return new Result(inserts, missing);
    }
}
