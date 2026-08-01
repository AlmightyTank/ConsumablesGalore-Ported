using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ConsumablesGalore;

/// <summary>
/// Replaces the old package.json. Holds the metadata the SPT 4.0 mod loader reads
/// when loading this mod. Every property must be overridden; unused ones may be null.
/// </summary>
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.musicmaniac.consumablesgalore";
    public override string Name { get; init; } = "MusicManiac-Consumables-Galore";
    public override string Author { get; init; } = "MusicManiac";
    public override List<string>? Contributors { get; init; } = ["AlmightyTank"];
    public override SemanticVersioning.Version Version { get; init; } = new("2.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/AlmightyTank/ConsumablesGalore";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
