using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ModularVests.Server;

public record ModularVestsMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.anamelash.modularvests";
    public string Name { get; init; } = "Modular Vests";
    public string Author { get; init; } = "anamelash";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.1.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }

    // The 6B45 and the Gladiator-S rigs are clones of the ones WTT-ContentBackport adds:
    // their templates and their model bundles both come from there.
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.wtt.contentbackport"] = new SemanticVersioning.Range("*"),
    };

    public string? Url { get; init; }
    public string License { get; init; } = "CC-BY-NC-SA-4.0";
}
