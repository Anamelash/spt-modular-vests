using System.Text.RegularExpressions;
using Xunit;

namespace ModularVests.Server.Tests;

/// <summary>
/// BepInEx throws on a section or key name with any of <c>= \n \t \ " ' [ ]</c> - in the
/// plugin that is the very first thing Awake does, and the whole mod stays off. The F12
/// settings are read from the sources, since the plugins cannot be loaded without the game.
/// </summary>
public partial class ConfigKeyTests
{
    private static readonly char[] Forbidden = ['=', '\n', '\t', '\\', '"', '\'', '[', ']'];

    [GeneratedRegex("""const string (\w+) = "((?:[^"\\]|\\.)*)";""")]
    private static partial Regex SectionConstant();

    [GeneratedRegex("""\.Bind\(\s*(\w+),\s*"((?:[^"\\]|\\.)*)"\s*,""")]
    private static partial Regex BindCall();

    public static IEnumerable<object[]> ConfigSources() =>
    [
        ["client/ModularVests.Client/ClientConfig.cs"],
        ["client/ModularVests.DevTools/DevConfig.cs"],
    ];

    [Theory]
    [MemberData(nameof(ConfigSources))]
    public void Section_and_key_names_are_legal(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        var sections = SectionConstant().Matches(source).ToDictionary(m => m.Groups[1].Value, m => Unescape(m.Groups[2].Value));

        var binds = BindCall().Matches(source);
        Assert.NotEmpty(binds);
        foreach (Match bind in binds)
        {
            Assert.True(sections.TryGetValue(bind.Groups[1].Value, out var section),
                $"{relativePath}: section {bind.Groups[1].Value} is not a string constant of the file");
            var key = Unescape(bind.Groups[2].Value);
            Assert.True(section.IndexOfAny(Forbidden) < 0, $"{relativePath}: section '{section}' has a forbidden character");
            Assert.True(key.IndexOfAny(Forbidden) < 0, $"{relativePath}: key '{key}' has a forbidden character");
        }
    }

    private static string Unescape(string literal) => Regex.Unescape(literal);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ModularVests.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("repository root (ModularVests.sln) not found above " + AppContext.BaseDirectory);
    }
}
