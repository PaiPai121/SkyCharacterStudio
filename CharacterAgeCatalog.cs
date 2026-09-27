using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sky1stCharacterStudio;

public sealed class CharacterAgeInfo
{
    [JsonPropertyName("status")] public string Status { get; init; } = "unknown";
    [JsonPropertyName("age")] public int? Age { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("basis")] public string Basis { get; init; } = "该模型尚无经核对的年龄记录；胸部调整仅向已确认成年的模型开放。";
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    [JsonIgnore] public bool DefaultedFromBaseGame { get; init; }
    public bool IsAdult => Status == "adult" && (!Age.HasValue || Age >= 18);
    public string DisplayText => UiText.AgeDisplay(this);
}

public static class CharacterAgeCatalog
{
    private static (bool Available, Dictionary<string, CharacterAgeInfo> Records) Load(string fileName) {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        if (!File.Exists(path)) return (false, new());
        var records = JsonSerializer.Deserialize<Dictionary<string, CharacterAgeInfo>>(File.ReadAllText(path)) ?? new();
        return (true, records);
    }
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> FirstCatalog =
        new(() => Load("character-ages.json"));
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> SecondCatalog =
        new(() => Load("character-ages-2nd.json"));

    public static CharacterAgeInfo Get(string modelId, bool isBaseGameCharacter = false, GameEdition edition = GameEdition.First)
    {
        var catalog = edition == GameEdition.Second ? SecondCatalog.Value : FirstCatalog.Value;
        if (catalog.Records.TryGetValue(modelId, out var info)) return info;
        return new();
    }
}
