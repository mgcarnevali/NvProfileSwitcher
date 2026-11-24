using System.Text.Json.Serialization;

namespace NvProfileSwitcher;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(NvProfileSwitcher.Core.DisplayModeRecovery))]
internal sealed partial class NvProfileSwitcherJsonContext : JsonSerializerContext
{
}
