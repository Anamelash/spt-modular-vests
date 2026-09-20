using System.Reflection;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace ModularVests.Server.Services;

/// <summary>
/// Whether the server still accepts new item templates.
///
/// Since 4.1.3 the server sets <c>CustomItemService.ProfilesLoaded</c> at the start of
/// SaveCallbacks and refuses (fatally) any item added afterwards: profiles are validated
/// against the item table, and an item they cannot resolve corrupts them. The mod is built
/// against 4.1.1, where the property does not exist yet — hence reflection. No property
/// means no cutoff, which is what those versions did.
/// </summary>
internal static class ItemRegistrationWindow
{
    private static readonly PropertyInfo? ProfilesLoaded = typeof(CustomItemService)
        .GetProperty("ProfilesLoaded", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>True while items may still be added to the database.</summary>
    public static bool IsOpen(CustomItemService customItemService) =>
        ProfilesLoaded?.GetValue(customItemService) as bool? != true;
}
