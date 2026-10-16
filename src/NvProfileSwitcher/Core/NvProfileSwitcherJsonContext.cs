using System.Text.Json.Serialization;

namespace NvProfileSwitcher;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class NvProfileSwitcherJsonContext : JsonSerializerContext
{
}
