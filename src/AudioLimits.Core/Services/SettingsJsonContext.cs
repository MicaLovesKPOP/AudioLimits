using System.Text.Json.Serialization;
using AudioLimits.Core.Models;

namespace AudioLimits.Core.Services;

// Compile-time JSON metadata keeps SettingsStore safe when the portable build
// is fully trimmed. Keep these DTOs in the context whenever a settings schema
// migration still needs to deserialize them.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LegacySettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}

internal sealed class LegacySettings
{
    public int SchemaVersion { get; set; } = 1;
    public List<LegacyLimit> Limits { get; set; } = new();
}

internal sealed class LegacyLimit
{
    public string EndpointGuid { get; set; } = "";
    public string? FriendlyName { get; set; }
    public double AttenuationDb { get; set; }
    public double EquivalentWindowsPercent { get; set; }
}
