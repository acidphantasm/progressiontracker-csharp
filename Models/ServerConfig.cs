using System.Text.Json.Serialization;

namespace _progressionTracker.Models;

public class ServerConfig
{
    [JsonPropertyName("configAppSettings")]
    public required ConfigAppSettings ConfigAppSettings { get; set; }
}

public class ConfigAppSettings
{
    [JsonPropertyName("disableAnimations")]
    public bool DisableAnimations { get; set; }
    [JsonPropertyName("allowUpdateChecks")]
    public bool AllowUpdateChecks { get; set; }
}