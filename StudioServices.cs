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
        var seed = FindSeedPreview(record.ModelId, adjusted);
        if (seed is not null)
        {
            var originalSeed = adjusted ? FindSeedPreview(record.ModelId, adjusted: false) : null;
            if (adjusted && originalSeed is not null
                && !string.Equals(Path.GetFullPath(seed), Path.GetFullPath(originalSeed), StringComparison.OrdinalIgnoreCase))
                return CreateComparisonBitmap(originalSeed, seed);
            return LoadBitmap(seed);
        }

        if (record.PreviewEntry is not null && imageArchive is not null)
        {
            try
            {
                var ddsPath = System.IO.Path.Combine(_cacheRoot, record.ModelId + ".dds");
                var pngPath = System.IO.Path.Combine(_cacheRoot, record.ModelId + ".png");
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

    private string? FindSeedPreview(string modelId, bool adjusted)
    {
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
        if (!File.Exists(script)) throw new FileNotFoundException("找不到 DDS 预览脚本", script);
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 DDS 预览脚本");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException((await process.StandardError.ReadToEndAsync(cancellationToken)).Trim());
    }

    internal static string ResolvePython()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "runtime", "python", "python.exe"),
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
            DrawText(drawing, "原版 / 调整后 3D 对照", new Point(25, 12), 22, titleBrush);
            DrawImagePanel(drawing, original, new Rect(left, 50, panelWidth, panelHeight), "原版", borderPen, titleBrush);
            DrawImagePanel(drawing, adjusted, new Rect(right, 50, panelWidth, panelHeight), "调整后（100%）", borderPen, titleBrush);

            var cropRect = new Int32Rect(385, 260, 330, 235);
            var originalCrop = CropBitmap(original, cropRect);
            var adjustedCrop = CropBitmap(adjusted, cropRect);
            DrawImagePanel(drawing, originalCrop, new Rect(left, 625, panelWidth, 245), "胸部局部放大 · 原版", borderPen, titleBrush);
            DrawImagePanel(drawing, adjustedCrop, new Rect(right, 625, panelWidth, 245), "胸部局部放大 · 调整后", borderPen, titleBrush);
            DrawText(drawing, "右侧是调整后模型；下方局部图用于观察细微轮廓差异。", new Point(25, 875), 12, mutedBrush);
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
            CultureInfo.GetCultureInfo("zh-CN"),
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
                $"{displayName}\n{modelId}\n\n暂无头像预览",
                CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
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
        CancellationToken cancellationToken, string mode = "width", bool enableSummon = true)
    {
        strength = Math.Clamp(strength, -500, 1000);
        var exportRoot = System.IO.Path.Combine(projectRoot, "exports", $"{record.ModelId}_{mode}_{strength:000}");
        var modelRoot = System.IO.Path.Combine(exportRoot, "asset", "common", "model");
        Directory.CreateDirectory(modelRoot);
        var modelPath = System.IO.Path.Combine(modelRoot, record.ModelFileName);
        var game=Directory.GetParent(Directory.GetParent(Directory.GetParent(modelArchive.Path)!.FullName)!.FullName)!.FullName;
        await AutoModelService.Run(game,record.ModelId,mode,modelRoot,strength);
        var shapeApplied=true;
        string helperMessage=$"自动模型调整：{mode}；按当前模型解析骨骼和顶点偏移。";

        var presetPath = System.IO.Path.Combine(exportRoot, "shape_preset.json");
        var preset = new ShapePreset
        {
            ModelId = record.ModelId,
            DisplayName = record.DisplayName,
            ShapeStrength = strength,
            SourceModelEntry = record.ModelEntry.Name,
            SourceArchive = modelArchive.Path,
            PreviewEntry = record.PreviewEntry?.Name,
            ShapeEditApplied = shapeApplied,
            GeneratedUtc = DateTime.UtcNow,
            Notes = record.IsSupportedShapeEdit
                ? helperMessage ?? "已从校验过的原版生成指定强度。"
                : "该角色尚未完成拓扑验证，本次只导出原版模型副本和参数预设。"
        };
        await File.WriteAllTextAsync(presetPath,
            JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);

        string? runtimePath = null;
        if (buildRuntime)
            runtimePath = await BuildRuntimePackageAsync(record, modelPath, exportRoot, projectRoot, game, strength, cancellationToken,enableSummon);

        var message = record.IsSupportedShapeEdit
            ? (shapeApplied ? $"已导出 {record.DisplayName} 的 {strength}% 形体模型。" : $"已导出模型副本；{helperMessage ?? "形体补丁未应用"}")
            : $"已导出 {record.DisplayName} 的原版副本，形体参数已写入预设（尚未验证该模型的拓扑）。";
        return new ExportResult
        {
            OutputDirectory = exportRoot,
            ModelPath = modelPath,
            PresetPath = presetPath,
            RuntimePackagePath = runtimePath,
            ShapeEditApplied = shapeApplied,
            Message = message
        };
    }

    private static async Task<string> BuildRuntimePackageAsync(CharacterRecord record, string modelPath, string exportRoot,
        string projectRoot, string game, int strength, CancellationToken cancellationToken, bool enableSummon)
    {
        var destination = System.IO.Path.Combine(exportRoot, "Scherazard_Runtime");
        var source = FindRuntimeRoot(projectRoot);
        if (source is not null)
        {
            CopyDirectory(source, destination);
            // Do not reset another character's installed model while applying this one.
            var packageModels=Path.Combine(destination,"Mod","ScherazardSummon","asset","common","model");
            if(!record.ModelId.Equals("chr5002",StringComparison.OrdinalIgnoreCase))
                File.Delete(Path.Combine(packageModels,"chr5002.mdl"));

            var runtimeModel = System.IO.Path.Combine(destination, "Mod", "ScherazardSummon", "asset", "common", "model", record.ModelFileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(runtimeModel)!);
            File.Copy(modelPath, runtimeModel, true);
            if(enableSummon)await StudioSummonService.ConfigureAsync(destination,game,record.ModelId,record.DisplayName);
            else {
                var ini=Path.Combine(destination,"ED9Loader","config","EventStarter.ini");
                File.WriteAllText(ini,"[Settings]\r\nenabled=0\r\n");
                File.WriteAllText(Path.Combine(destination,"Mod","ScherazardSummon","add_dat_ini.json"),"{\"inject\":[]}");
            }
            await File.WriteAllTextAsync(System.IO.Path.Combine(destination, "CharacterStudioOverride.txt"),
                $"模型：{record.DisplayName} ({record.ModelId})\r\n形体强度：{strength}%\r\n\r\n这是离线生成的测试包副本。请先退出游戏，再手工将此目录内容复制到游戏目录；工具不会自动安装或启动游戏。\r\n",
                Encoding.UTF8, cancellationToken);
        }
        else throw new DirectoryNotFoundException("找不到已验证的游戏运行组件");
        return destination;
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
