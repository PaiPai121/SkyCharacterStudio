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
    public string DisplayText => UiText.AgeDisplay(this);
}

public static class CharacterAgeCatalog
{
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> Catalog = new(() => {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "character-ages.json");
        if (!File.Exists(path)) return (false, new());
        var records = JsonSerializer.Deserialize<Dictionary<string, CharacterAgeInfo>>(File.ReadAllText(path)) ?? new();
        return (true, records);
    });
    public static CharacterAgeInfo Get(string modelId, bool isBaseGameCharacter = false)
    {
        var catalog = Catalog.Value;
        if (catalog.Records.TryGetValue(modelId, out var info)) return info;
        return new();
    }
}
