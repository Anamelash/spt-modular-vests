using System.Security.Cryptography;
using System.Text;

namespace ModularVests.Server.Services;

/// <summary>
/// Every id the mod puts into the database (templates, slots, grids, assort rows, the trader, presets) is
/// derived from a readable key instead of being written down or generated.
///
/// INVARIANT, forever: the algorithm and the prefix must never change. Profiles store
/// these ids — a different id for the same key orphans every rig, pouch and item inside
/// them on the next server start.
/// </summary>
public static class DeterministicId
{
    public const string Prefix = "modularvests:";

    /// <summary>24 lowercase hex characters: the first 12 bytes of SHA-1("modularvests:" + key).</summary>
    public static string For(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(Prefix + key));
        return Convert.ToHexStringLower(hash, 0, 12);
    }

    // --- the key vocabulary, in one place so the tests enumerate exactly what ships ---

    public static string VestKey(string vest) => $"vest:{vest}";

    public static string VestSlotKey(string vest, string slot) => $"vest:{vest}:slot:{slot}";

    public static string PouchKey(string pouch) => $"pouch:{pouch}";

    public static string PouchGridKey(string pouch, string grid) => $"pouch:{pouch}:grid:{grid}";

    public static string AssortKey(string itemKey) => $"assort:{itemKey}";

    /// <summary>A built-in insert that ships inside the item the trader sells.</summary>
    public static string AssortSlotKey(string itemKey, string slot) => $"{AssortKey(itemKey)}:slot:{slot}";

    /// <summary>The trader's id ends up in profiles (TradersInfo): a shipped trader key is never renamed.</summary>
    public static string TraderKey(string trader) => $"trader:{trader}";

    /// <summary>The default preset of a rig (globals.ItemPresets).</summary>
    public static string PresetKey(string vest) => $"preset:{vest}";

    public static string PresetRootKey(string vest) => $"{PresetKey(vest)}:root";

    public static string PresetSlotKey(string vest, string slot) => $"{PresetKey(vest)}:slot:{slot}";
}
