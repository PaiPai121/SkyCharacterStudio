using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sky1stCharacterStudio;

public sealed class PreviewService
{
    private readonly string _projectRoot;
    private readonly string _cacheRoot;

    public PreviewService(string projectRoot)
    {
        _projectRoot = projectRoot;
        _cacheRoot = System.IO.Path.Combine(projectRoot, "cache", "previews");
        Directory.CreateDirectory(_cacheRoot);
    }

    public async Task<BitmapSource> LoadAsync(
        CharacterRecord record,
        PacArchive? imageArchive,
        CancellationToken cancellationToken,
        bool adjusted = false)
    {
        var seed = FindSeedPreview(record.Edition, record.ModelId, adjusted);
        if (seed is not null)
        {
            var originalSeed = adjusted ? FindSeedPreview(record.Edition, record.ModelId, adjusted: false) : null;
            if (adjusted && originalSeed is not null
                && !string.Equals(Path.GetFullPath(seed), Path.GetFullPath(originalSeed), StringComparison.OrdinalIgnoreCase))
                return CreateComparisonBitmap(originalSeed, seed);
            return LoadBitmap(seed);
        }

        if (record.PreviewEntry is not null && imageArchive is not null)
        {
            try
            {
                var editionCache = System.IO.Path.Combine(_cacheRoot, record.Edition.ToString());
                Directory.CreateDirectory(editionCache);
                var ddsPath = System.IO.Path.Combine(editionCache, record.ModelId + ".dds");
                var pngPath = System.IO.Path.Combine(editionCache, record.ModelId + ".png");
                if (!File.Exists(ddsPath))
                    await File.WriteAllBytesAsync(ddsPath, imageArchive.ReadEntry(record.PreviewEntry), cancellationToken);
                if (!File.Exists(pngPath) || File.GetLastWriteTimeUtc(pngPath) < File.GetLastWriteTimeUtc(ddsPath))
                    await ConvertDdsAsync(ddsPath, pngPath, cancellationToken);
                if (File.Exists(pngPath)) return LoadBitmap(pngPath);
            }
            catch
            {
                // A missing Python/Pillow installation should only remove the
                // portrait preview; the scanner and contour editor still work.
            }
        }

        return CreatePlaceholder(record.ModelId, record.DisplayName);
    }

    private string? FindSeedPreview(GameEdition edition, string modelId, bool adjusted)
    {
        if (edition == GameEdition.Second) return null;
        var candidates = adjusted
            ? new[]
            {
                System.IO.Path.Combine(_projectRoot, "assets", "previews", modelId + "_after.png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "previews", modelId + "_after.png"),
                System.IO.Path.Combine(_projectRoot, "assets", "previews", modelId + ".png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "previews", modelId + ".png")
            }
            : new[]
            {
                System.IO.Path.Combine(_projectRoot, "assets", "previews", modelId + ".png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "previews", modelId + ".png")
            };
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task ConvertDdsAsync(string input, string output, CancellationToken cancellationToken)
    {
        var script = FindFileUpwards("tools", "preview_dds.py")
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "preview_dds.py");
        if (!File.Exists(script)) throw new FileNotFoundException(UiText.T("error.preview.script"), script);
        var python = ResolvePython();
        var start = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(input);
        start.ArgumentList.Add(output);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(UiText.T("error.preview.start"));
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException((await process.StandardError.ReadToEndAsync(cancellationToken)).Trim());
    }

    internal static string ResolvePython()
    {
        var toolchain = Environment.GetEnvironmentVariable("SKY_STUDIO_TOOLCHAIN");
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "runtime", "python", "python.exe"),
            Environment.GetEnvironmentVariable("SKY_STUDIO_PYTHON"),
            string.IsNullOrWhiteSpace(toolchain) ? null : System.IO.Path.Combine(toolchain, ".venv", "Scripts", "python.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
            FindFileUpwards(".venv", "Scripts", "python.exe"),
            FindFileUpwards("..", "Sky1st-Scherazard-Mod", ".venv", "Scripts", "python.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Sky1st-Scherazard-Mod", ".venv", "Scripts", "python.exe")
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path)) ?? "python.exe";
    }

    internal static string? FindFileUpwards(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = parts.Aggregate(directory.FullName, System.IO.Path.Combine);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource CreateComparisonBitmap(string originalPath, string adjustedPath)
    {
        var original = LoadBitmap(originalPath);
        var adjusted = LoadBitmap(adjustedPath);
        const int width = 1200;
        const int height = 900;
        const double panelWidth = 555;
        const double panelHeight = 535;
        const double left = 25;
        const double right = 620;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 19, 30)), null, new Rect(0, 0, width, height));
            var titleBrush = new SolidColorBrush(Color.FromRgb(215, 177, 91));
            var mutedBrush = new SolidColorBrush(Color.FromRgb(145, 172, 182));
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(77, 124, 132)), 2);
            DrawText(drawing, UiText.T("comparison.title"), new Point(25, 12), 22, titleBrush);
            DrawImagePanel(drawing, original, new Rect(left, 50, panelWidth, panelHeight), UiText.T("comparison.original"), borderPen, titleBrush);
            DrawImagePanel(drawing, adjusted, new Rect(right, 50, panelWidth, panelHeight), UiText.T("comparison.adjusted"), borderPen, titleBrush);

            var cropRect = new Int32Rect(385, 260, 330, 235);
            var originalCrop = CropBitmap(original, cropRect);
            var adjustedCrop = CropBitmap(adjusted, cropRect);
            DrawImagePanel(drawing, originalCrop, new Rect(left, 625, panelWidth, 245), UiText.T("comparison.crop.original"), borderPen, titleBrush);
            DrawImagePanel(drawing, adjustedCrop, new Rect(right, 625, panelWidth, 245), UiText.T("comparison.crop.adjusted"), borderPen, titleBrush);
            DrawText(drawing, UiText.T("comparison.note"), new Point(25, 875), 12, mutedBrush);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CropBitmap(BitmapSource source, Int32Rect requested)
    {
        var x = Math.Clamp(requested.X, 0, Math.Max(0, source.PixelWidth - 1));
        var y = Math.Clamp(requested.Y, 0, Math.Max(0, source.PixelHeight - 1));
        var width = Math.Clamp(requested.Width, 1, source.PixelWidth - x);
        var height = Math.Clamp(requested.Height, 1, source.PixelHeight - y);
        var crop = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        crop.Freeze();
        return crop;
    }

    private static void DrawImagePanel(
        DrawingContext drawing,
        BitmapSource image,
        Rect panel,
        string label,
        Pen borderPen,
        Brush labelBrush)
    {
        var background = new SolidColorBrush(Color.FromRgb(22, 32, 43));
        drawing.DrawRoundedRectangle(background, borderPen, panel, 8, 8);
        var imageRect = new Rect(panel.X + 8, panel.Y + 36, panel.Width - 16, panel.Height - 44);
        var scale = Math.Min(imageRect.Width / image.PixelWidth, imageRect.Height / image.PixelHeight);
        var fitted = new Rect(imageRect.X + (imageRect.Width - image.PixelWidth * scale) / 2,
            imageRect.Y + (imageRect.Height - image.PixelHeight * scale) / 2,
            image.PixelWidth * scale, image.PixelHeight * scale);
        drawing.DrawImage(image, fitted);
        DrawText(drawing, label, new Point(panel.X + 12, panel.Y + 8), 16, labelBrush);
    }

    private static void DrawText(DrawingContext drawing, string text, Point origin, double size, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            UiText.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            size,
            brush,
            1.0);
        drawing.DrawText(formatted, origin);
    }

    public static BitmapSource CreatePlaceholder(string modelId, string displayName)
    {
        const int size = 560;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(22, 38, 53), Color.FromRgb(10, 20, 31), 90);
            drawing.DrawRectangle(background, null, new Rect(0, 0, size, size));
            var accent = new Pen(new SolidColorBrush(Color.FromRgb(194, 157, 83)), 2);
            drawing.DrawRoundedRectangle(null, accent, new Rect(24, 24, size - 48, size - 48), 18, 18);
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 83, 201, 187)), null, new Point(size / 2, 218), 94, 94);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(65, 72, 130, 158)), null, new Rect(188, 306, 184, 142));
            var label = new FormattedText(
                $"{UiText.CharacterName(modelId, displayName)}\n{modelId}\n\n{UiText.T("placeholder.no.portrait")}",
                UiText.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"), 22, new SolidColorBrush(Color.FromRgb(224, 235, 239)),
                1.0);
            label.TextAlignment = TextAlignment.Center;
            drawing.DrawText(label, new Point((size - label.Width) / 2, 103));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

public static class ExportService
{
    public static async Task<ExportResult> ExportAsync(
        CharacterRecord record, PacArchive modelArchive, string projectRoot, int strength, bool buildRuntime,
        CancellationToken cancellationToken, string mode = "width", bool enableSummon = true,
        string? secondLoaderPath = null)
    {
        strength = Math.Clamp(strength, -500, 1000);
        var exportRoot = record.Edition == GameEdition.Second
            ? System.IO.Path.Combine(projectRoot, "exports", "second", $"{record.ModelId}_{mode}_{strength:000}")
            : System.IO.Path.Combine(projectRoot, "exports", $"{record.ModelId}_{mode}_{strength:000}");
        var modelRoot = System.IO.Path.Combine(exportRoot, "asset", "common", "model");
        Directory.CreateDirectory(modelRoot);
        var modelPath = System.IO.Path.Combine(modelRoot, record.ModelFileName);
        var game=GameEditionInfo.RootFromModelArchive(modelArchive.Path);
        await AutoModelService.Run(game,record.ModelId,mode,modelRoot,strength,record.IsBaseGameCharacter,
            modelSource:record.ModelEntry.LoosePath ?? modelArchive.ArchivePath,
            ageDefinitionLabel:record.AgeDefinitionLabel);
        var shapeApplied=true;
        string helperMessage = UiText.T(mode.Equals("chest", StringComparison.OrdinalIgnoreCase)
            ? "export.helper.chest" : "export.helper.width");

        var presetPath = System.IO.Path.Combine(exportRoot, "shape_preset.json");
        var preset = new ShapePreset
        {
            ModelId = record.ModelId,
            DisplayName = record.LocalizedName,
            ShapeStrength = strength,
            SourceModelEntry = record.ModelEntry.Name,
            SourceArchive = record.ModelEntry.LoosePath ?? modelArchive.ArchivePath ?? modelArchive.Path,
            PreviewEntry = record.PreviewEntry?.Name,
            ShapeEditApplied = shapeApplied,
            GeneratedUtc = DateTime.UtcNow,
            Notes = record.IsSupportedShapeEdit
                ? $"{helperMessage} {UiText.T("export.notes.verified")}"
                : UiText.T("export.notes.unverified")
        };
        await File.WriteAllTextAsync(presetPath,
            JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);

        string? runtimePath = null;
        SummonSetupResult? summonSetup = null;
        if (buildRuntime)
            (runtimePath, summonSetup) = await BuildRuntimePackageAsync(record, modelPath, exportRoot, projectRoot, game, strength, cancellationToken,enableSummon,secondLoaderPath);

        var message = record.IsSupportedShapeEdit
            ? (shapeApplied ? UiText.F("export.message", record.LocalizedName, strength) : UiText.T("export.copy.message"))
            : UiText.F("export.unverified.message", record.LocalizedName);
        return new ExportResult
        {
            OutputDirectory = exportRoot,
            ModelPath = modelPath,
            PresetPath = presetPath,
            RuntimePackagePath = runtimePath,
            ShapeEditApplied = shapeApplied,
            SummonEnabled = summonSetup?.Enabled ?? false,
            SummonWarning = summonSetup?.DisabledReason,
            Message = message
        };
    }

    private static async Task<(string Path, SummonSetupResult Summon)> BuildRuntimePackageAsync(CharacterRecord record, string modelPath, string exportRoot,
        string projectRoot, string game, int strength, CancellationToken cancellationToken, bool enableSummon,
        string? secondLoaderPath)
    {
        if (record.Edition == GameEdition.Second)
        {
            if (string.IsNullOrWhiteSpace(secondLoaderPath))
                throw new FileNotFoundException(UiText.T("error.second.loader.missing"));
            var sourceHash = SecondLoaderService.Validate(secondLoaderPath);
            var secondPackage = Path.Combine(exportRoot, "Sora2nd_Runtime");
            var secondModel = Path.Combine(secondPackage, "asset", "common", "model", record.ModelFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(secondModel)!);
            File.Copy(modelPath, secondModel, true);
            var loaderCopy = Path.Combine(secondPackage, "xinput1_4.dll");
            File.Copy(secondLoaderPath, loaderCopy, true);
            if (!SecondLoaderService.Validate(loaderCopy).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException(UiText.T("error.second.loader.copy"));
            return (secondPackage, new SummonSetupResult(false, null));
        }
        // Each export gets a fresh package so stale optional plugins from an
        // earlier export can never be installed with a later unchecked F8 run.
        var destination = Path.Combine(exportRoot, "Sora1st_Runtime-" + Guid.NewGuid().ToString("N")[..8]);
        var source = FindRuntimeRoot(projectRoot)
            ?? throw new DirectoryNotFoundException(UiText.T("error.no.runtime"));
        var build = GameInstaller.GetSupportedFirstVersion(game);
        var redirectName = build == "1.0.7.0" ? "StudioModelRedirect.dll" : "SceneRedirect.dll";
        var redirectSource = FindFirstRuntimeFile(projectRoot,
            $"runtime/first-redirect/{build}/{redirectName}");
        if (redirectSource is null && build == "1.0.5.0")
            redirectSource = Path.Combine(source, "ED9Loader", "plugins", "SceneRedirect.dll");
        if (redirectSource is null || !File.Exists(redirectSource))
            throw new FileNotFoundException($"No verified 1st model redirect for game build {build}.");

        CopyRuntimeFile(Path.Combine(source, "xinput1_4.dll"), destination, "xinput1_4.dll");
        CopyRuntimeFile(Path.Combine(source, "ED9Loader", "ED9ModManager.exe"), destination,
            "ED9Loader/ED9ModManager.exe");
        CopyRuntimeFile(redirectSource, destination, $"ED9Loader/plugins/{redirectName}");
        CopyRuntimeFile(modelPath, destination,
            $"Mod/SkyCharacterStudio/asset/common/model/{record.ModelFileName}");

        SummonSetupResult summonSetup;
        if (enableSummon && build == "1.0.5.0")
        {
            var summonRoot = FindFirstRuntimeFile(projectRoot,
                "runtime/first-summon/ED9Loader/plugins/EventStarter.dll");
            var scriptSource = FindFirstRuntimeFile(projectRoot,
                "runtime/first-summon/ED9Loader/plugins/ScriptInject.dll");
            var iniSource = FindFirstRuntimeFile(projectRoot,
                "runtime/first-summon/ED9Loader/config/EventStarter.ini");
            summonRoot ??= Path.Combine(source, "ED9Loader", "plugins", "EventStarter.dll");
            scriptSource ??= Path.Combine(source, "ED9Loader", "plugins", "ScriptInject.dll");
            iniSource ??= Path.Combine(source, "ED9Loader", "config", "EventStarter.ini");
            CopyRuntimeFile(summonRoot, destination, "ED9Loader/plugins/EventStarter.dll");
            CopyRuntimeFile(scriptSource, destination, "ED9Loader/plugins/ScriptInject.dll");
            CopyRuntimeFile(iniSource, destination, "ED9Loader/config/EventStarter.ini");
            CopyRuntimeFile(modelPath, destination,
                $"Mod/ScherazardSummon/asset/common/model/{record.ModelFileName}");
            summonSetup = await StudioSummonService.ConfigureOptionalAsync(destination, game,
                record.ModelId, record.LocalizedName, true);
            if (!summonSetup.Enabled)
            {
                File.Delete(Path.Combine(destination, "ED9Loader", "plugins", "EventStarter.dll"));
                File.Delete(Path.Combine(destination, "ED9Loader", "plugins", "ScriptInject.dll"));
                File.Delete(Path.Combine(destination, "Mod", "ScherazardSummon", "add_dat_ini.json"));
                File.Delete(Path.Combine(destination, "Mod", "ScherazardSummon", "asset", "common",
                    "model", record.ModelFileName));
                File.Delete(Path.Combine(destination, "ED9Loader", "config", "EventStarter.ini"));
                StagePreviousStudioSummonDisable(game, destination);
            }
        }
        else
        {
            summonSetup = new SummonSetupResult(false, enableSummon
                ? $"F8 summon is unavailable for game build {build}; the model will still be installed."
                : null);
            StagePreviousStudioSummonDisable(game, destination);
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "CharacterStudioOverride.txt"),
            UiText.F("runtime.override", record.LocalizedName, record.ModelId, strength),
            Encoding.UTF8, cancellationToken);
        return (destination, summonSetup);
    }

    private static string? FindFirstRuntimeFile(string projectRoot, string relative)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, projectRoot })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return path;
        }
        if (relative.StartsWith("runtime/", StringComparison.Ordinal))
        {
            var sourceRelative = "runtime-source/" + relative["runtime/".Length..];
            var source = Path.Combine(projectRoot,
                sourceRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(source)) return source;
        }
        return null;
    }

    private static void CopyRuntimeFile(string source, string destinationRoot, string relative)
    {
        if (!File.Exists(source)) throw new FileNotFoundException(UiText.T("error.no.runtime"), source);
        var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void StagePreviousStudioSummonDisable(string game, string destination)
    {
        var selection = Path.Combine(game, "Mod", "ScherazardSummon", "character-studio-selection.json");
        if (!File.Exists(selection)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(selection));
            if (!doc.RootElement.TryGetProperty("installedSelection", out var installed) ||
                installed.ValueKind != JsonValueKind.True ||
                !doc.RootElement.TryGetProperty("editedEvent", out var edited) ||
                edited.GetString() != "StudioSummon") return;
        }
        catch (JsonException) { return; }
        var ini = Path.Combine(destination, "ED9Loader", "config", "EventStarter.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, "[Settings]\r\nenabled=0\r\n", Encoding.ASCII);
        var legacyMod = Path.Combine(destination, "Mod", "ScherazardSummon", "add_dat_ini.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyMod)!);
        File.WriteAllText(legacyMod, "{\"inject\":[]}", Encoding.ASCII);
    }

    private static string? FindMeshRoot(string projectRoot)
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(projectRoot, "..", "Sky1st-Scherazard-Mod", "work", "chr5002"),
            System.IO.Path.Combine(projectRoot, "..", "..", "Sky1st-Scherazard-Mod", "work", "chr5002"),
            System.IO.Path.Combine(projectRoot, "Sky1st-Scherazard-Mod", "work", "chr5002"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Sky1st-Scherazard-Mod", "work", "chr5002")
        };
        return candidates.Select(System.IO.Path.GetFullPath).FirstOrDefault(path => File.Exists(System.IO.Path.Combine(path, "mesh_info.json")));
    }

    private static string? FindRuntimeRoot(string projectRoot)
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory,"runtime","mod-template"),
            System.IO.Path.Combine(projectRoot, "..", "Sky1st-Scherazard-Mod", "dist", "Scherazard_Runtime"),
            System.IO.Path.Combine(projectRoot, "..", "..", "Sky1st-Scherazard-Mod", "dist", "Scherazard_Runtime"),
            System.IO.Path.Combine(projectRoot, "Sky1st-Scherazard-Mod", "dist", "Scherazard_Runtime")
        };
        return candidates.Select(System.IO.Path.GetFullPath).FirstOrDefault(Directory.Exists);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(directory)));
    }
}
