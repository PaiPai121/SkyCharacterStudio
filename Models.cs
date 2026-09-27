using System.Text.Json.Serialization;

namespace Sky1stCharacterStudio;

public sealed class PacEntry
{
    public string Name { get; init; } = "";
    public long Offset { get; init; }
    public long Size { get; init; }
    public ulong Hash { get; init; }

    public override string ToString() => $"{Name} ({Size:N0} bytes)";
}

public sealed class CharacterRecord
{
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string LocalizedName => UiText.CharacterName(ModelId, DisplayName);
    public PacEntry ModelEntry { get; init; } = new();
    public PacEntry? ModelInfoEntry { get; init; }
    public PacEntry? PreviewEntry { get; init; }
    public bool IsSupportedShapeEdit { get; init; }
    public bool IsBaseGameCharacter { get; init; }
    public GameEdition Edition { get; init; } = GameEdition.First;

    public string ModelFileName => $"{ModelId}.mdl";
    public string ArchiveSizeText => $"{ModelEntry.Size / 1024d / 1024d:0.00} MB";
    public string PreviewText => PreviewEntry is null ? UiText.T("portrait.missing") : UiText.T("portrait.found");
    public CharacterAgeInfo AgeInfo => CharacterAgeCatalog.Get(ModelId, IsBaseGameCharacter, Edition);
    public bool AdultShapeEligible => AgeInfo.IsAdult;
    public string SupportText => UiText.T("support");

    public override string ToString() => $"{LocalizedName}  ·  {ModelId}";
}

public sealed class ShapePreset
{
    [JsonPropertyName("model_id")]
    public string ModelId { get; init; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("shape_strength")]
    public int ShapeStrength { get; init; }

    [JsonPropertyName("source_model_entry")]
    public string SourceModelEntry { get; init; } = "";

    [JsonPropertyName("source_archive")]
    public string SourceArchive { get; init; } = "";

    [JsonPropertyName("preview_entry")]
    public string? PreviewEntry { get; init; }

    [JsonPropertyName("shape_edit_applied")]
    public bool ShapeEditApplied { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTime GeneratedUtc { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = "";
}

public sealed class ExportResult
{
    public string OutputDirectory { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string PresetPath { get; init; } = "";
    public string? RuntimePackagePath { get; init; }
    public bool ShapeEditApplied { get; init; }
    public bool SummonEnabled { get; init; }
    public string? SummonWarning { get; init; }
    public string Message { get; init; } = "";
}
