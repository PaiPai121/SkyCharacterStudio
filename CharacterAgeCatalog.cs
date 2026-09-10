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
    [JsonIgnore] public bool DefaultedFromBaseGame { get; init; }
    public bool IsAdult => Status == "adult" && (!Age.HasValue || Age >= 18);
    public string DisplayText => (Status switch {
        "adult" when IsAdult && DefaultedFromBaseGame => "原生未登记角色 · 默认成年",
        "adult" when IsAdult => "已确认成年",
        "minor" => "已确认未成年",
        _ => "年龄资料不足"
    }) + (Age.HasValue ? $" · {Age} 岁" : "");
}

public static class CharacterAgeCatalog
{
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> Catalog = new(() => {
        var path = PreviewService.FindFileUpwards("assets", "character-ages.json");
        if (path is null) return (false, new());
        var records = JsonSerializer.Deserialize<Dictionary<string, CharacterAgeInfo>>(File.ReadAllText(path)) ?? new();
        return (true, records);
    });
    public static CharacterAgeInfo Get(string modelId, bool isBaseGameCharacter = false)
    {
        var catalog = Catalog.Value;
        if (catalog.Records.TryGetValue(modelId, out var info)) return info;
        return isBaseGameCharacter && catalog.Available
            ? new CharacterAgeInfo {
                Status = "adult",
                Name = modelId,
                Basis = "由游戏原始模型归档扫描发现；年龄目录未将其标记为未成年",
                Source = "游戏原始 asset_common_model.pac",
                DefaultedFromBaseGame = true
            }
            : new();
    }
}
