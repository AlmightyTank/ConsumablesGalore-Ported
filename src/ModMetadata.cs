using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ConsumablesGalore;

/// <summary>
/// Replaces the old package.json. Holds the metadata the SPT mod loader reads
/// when loading this mod. Every property must be implemented; unused ones may be null.
/// </summary>
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.musicmaniac.consumablesgalore";
    public string Name { get; init; } = "MusicManiac-Consumables-Galore";
    public string Author { get; init; } = "MusicManiac";
    public List<string>? Contributors { get; init; } = ["AlmightyTank"];
    public SemanticVersioning.Version Version { get; init; } = new("3.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.1");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/AlmightyTank/ConsumablesGalore";
    public string License { get; init; } = "MIT";
}
