using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sky1stCharacterStudio;

public sealed class CharacterAgeInfo
{
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("age")] public int? Age { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("basis")] public string Basis { get; init; } = "";
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    /// <summary>True when the age catalog itself supplied this record, so its basis and status were reviewed.</summary>
    [JsonIgnore] public bool Catalogued { get; init; }
    /// <summary>Kept for the historical API name; equivalent to <see cref="Catalogued"/> being false.</summary>
    [JsonIgnore] public bool DefaultedFromBaseGame => !Catalogued;
    /// <summary>Only a catalogued minor is refused; every other record counts as an adult.</summary>
    public bool IsAdult => CharacterAgeCatalog.AllowsChestEditing(Status);
    public string DisplayText => UiText.AgeDisplay(this);
}

public static class CharacterAgeCatalog
{
    /// <summary>Unlisted-model wording. Mirrored by tools/character_age.py; any edit must be made in both places.</summary>
    public const string DefaultAdultBasis = "年龄目录未登记该模型；目录未将其标为未成年，故按默认成年处理";
    public const string DefaultAdultSource = "游戏原始 asset_common_model.pac";
    /// <summary>Policy: only a catalogued minor blocks chest editing; the catalog is a deny-list of minors.</summary>
    public static bool AllowsChestEditing(string? status) =>
        !string.Equals(status, "minor", StringComparison.OrdinalIgnoreCase);
    private static readonly CharacterAgeInfo DefaultAdult = new()
    {
        Status = "defaulted-adult",
        Age = null,
        Basis = DefaultAdultBasis,
        Source = DefaultAdultSource
    };
    private static (bool Available, Dictionary<string, CharacterAgeInfo> Records) Load(string fileName) {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        if (!File.Exists(path)) return (false, new());
        var records = JsonSerializer.Deserialize<Dictionary<string, CharacterAgeInfo>>(File.ReadAllText(path)) ?? new();
        foreach (var pair in records)
            records[pair.Key] = new CharacterAgeInfo
            {
                Status = pair.Value.Status,
                Age = pair.Value.Age,
                Name = pair.Value.Name,
                Basis = pair.Value.Basis,
                Source = pair.Value.Source,
                Catalogued = true
            };
        return (true, records);
    }
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> FirstCatalog =
        new(() => Load("character-ages.json"));
    private static readonly Lazy<(bool Available, Dictionary<string, CharacterAgeInfo> Records)> SecondCatalog =
        new(() => Load("character-ages-2nd.json"));

    public static CharacterAgeInfo Get(string modelId, bool isBaseGameCharacter = false,
        GameEdition edition = GameEdition.First, string? definitionLabel = null)
    {
        var catalog = edition == GameEdition.Second ? SecondCatalog.Value : FirstCatalog.Value;
        if (catalog.Records.TryGetValue(modelId, out var info)) return info;
        // A costume inherits a person's age only when the game's *definition*
        // names that person. A matching numeric prefix or scene alias alone is
        // not identity evidence: some resources reuse another person's parts.
        if (isBaseGameCharacter && modelId.Length == 11
            && modelId.AsSpan(0, 3).Equals("chr", StringComparison.OrdinalIgnoreCase)
            && modelId.AsSpan(3, 4).ToString().All(char.IsAsciiDigit)
            && modelId[7] == '_' && modelId[8] is 'c' or 'C'
            && char.IsAsciiDigit(modelId[9]) && char.IsAsciiDigit(modelId[10])
            && catalog.Records.TryGetValue(modelId[..7], out var person)
            && !string.IsNullOrWhiteSpace(person.Name)
            && (definitionLabel?.StartsWith(person.Name + "：", StringComparison.Ordinal) == true
                || definitionLabel?.StartsWith(person.Name + ":", StringComparison.Ordinal) == true))
        {
            return new CharacterAgeInfo
            {
                Status = person.Status,
                Age = person.Age,
                Name = person.Name,
                Basis = $"游戏名称表将该服装定义为「{definitionLabel}」；{person.Basis}",
                Source = person.Source,
                Catalogued = true
            };
        }
        return DefaultAdult;
    }
}
