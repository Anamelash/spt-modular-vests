using System.Reflection;

namespace ModularVests.Server.Tests;

internal static class TestPaths
{
    /// <summary>The shipped line-up, copied next to the test assembly.</summary>
    public static string ItemsConfig => Path.Combine(AppContext.BaseDirectory, "items.jsonc");

    /// <summary>The shipped trader, copied next to the test assembly.</summary>
    public static string TraderConfig => Path.Combine(AppContext.BaseDirectory, "trader.jsonc");

    /// <summary>The shipped bot and loot numbers, copied next to the test assembly.</summary>
    public static string BotsConfig => Path.Combine(AppContext.BaseDirectory, "bots.jsonc");

    /// <summary>The bundle manifest the mod ships, copied next to the test assembly.</summary>
    public static string BundleManifest => Path.Combine(AppContext.BaseDirectory, "bundles.json");

    /// <summary>SptGameDir from Directory.Build.props, or null when the install is absent.</summary>
    public static string? GameDir
    {
        get
        {
            var dir = typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "SptGameDir")?.Value;
            return dir != null && Directory.Exists(dir) ? dir : null;
        }
    }
}
