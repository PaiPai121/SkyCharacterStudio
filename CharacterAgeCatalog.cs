using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sky1stCharacterStudio;

public sealed class CharacterAgeInfo
{
    [JsonPropertyName("status")] public string Status { get; init; } = "unknown";
    [JsonPropertyName("age")] public int? Age { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("basis")] public string Basis { get; init; } = "尚无可核对的本作年龄资料";
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    public bool IsAdult => Status == "adult" && (!Age.HasValue || Age >= 18);
    public string DisplayText => (Status switch {
        "adult" when IsAdult => "已确认成年",
        "minor" => "已确认未成年",
        _ => "年龄资料不足"
    }) + (Age.HasValue ? $" · {Age} 岁" : "");
}

public static class CharacterAgeCatalog
{
    private static readonly Lazy<Dictionary<string, CharacterAgeInfo>> Records = new(() => {
        var path = PreviewService.FindFileUpwards("assets", "character-ages.json");
        if (path is null) return new();
        return JsonSerializer.Deserialize<Dictionary<string, CharacterAgeInfo>>(File.ReadAllText(path)) ?? new();
    });
    public static CharacterAgeInfo Get(string modelId) =>
        Records.Value.TryGetValue(modelId, out var info) ? info : new();
}
