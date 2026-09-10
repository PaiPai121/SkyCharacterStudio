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
    public PacEntry ModelEntry { get; init; } = new();
    public PacEntry? ModelInfoEntry { get; init; }
    public PacEntry? PreviewEntry { get; init; }
    public bool IsSupportedShapeEdit { get; init; }
    public bool IsBaseGameCharacter { get; init; }

    public string ModelFileName => $"{ModelId}.mdl";
    public string ArchiveSizeText => $"{ModelEntry.Size / 1024d / 1024d:0.00} MB";
    public string PreviewText => PreviewEntry is null ? "未找到角色头像，使用轮廓预览" : "已找到角色头像贴图";
    public CharacterAgeInfo AgeInfo => CharacterAgeCatalog.Get(ModelId, IsBaseGameCharacter);
    public bool AdultShapeEligible => AgeInfo.IsAdult;
    public string SupportText => "自动读取模型、材质和骨骼；加载成功后可调整";

    public override string ToString() => $"{DisplayName}  ·  {ModelId}";
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
    public string Message { get; init; } = "";
}
