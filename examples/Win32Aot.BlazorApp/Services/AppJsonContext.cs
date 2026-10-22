using System.Text.Json.Serialization;

namespace Win32Aot.BlazorApp.Services;

public sealed record BrowserInfo(string UserAgent, string Language, int ScreenWidth, int ScreenHeight, double DevicePixelRatio);

/// <summary>
/// Source-generated System.Text.Json metadata: the AOT-safe way to (de)serialize custom types.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserInfo))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
