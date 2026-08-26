using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace LootingBotsServerMod;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "me.skwizzy.lootingbots";
    public string Name { get; init; } = "LootingBots";
    public string Author { get; init; } = "Skwizzy";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.8.0");
    public Range SptVersion { get; init; } = new("~4.1.3");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/Skwizzy/SPT-LootingBots";
    public bool? IsBundleMod { get; init; } = false;
    public string License { get; init; } = "MIT";
}
