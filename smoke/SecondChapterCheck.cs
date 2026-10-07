using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sky1stCharacterStudio;

internal static class SecondChapterCheck
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Pump(Func<bool> done, string stage)
    {
        var start = DateTime.UtcNow;
        var nextProgress = start.AddSeconds(10);
        while (!done())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromMinutes(4))
                throw new TimeoutException($"{stage} exceeded four minutes");
            if (DateTime.UtcNow >= nextProgress)
            {
                Console.WriteLine($"STAGE {stage} elapsed={(int)(DateTime.UtcNow - start).TotalSeconds}s");
                nextProgress = DateTime.UtcNow.AddSeconds(10);
            }
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    private static void WriteSingleModelPac(string path, PacEntry entry, byte[] model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var name = Encoding.UTF8.GetBytes(entry.Name);
        var offset = checked((ulong)(48 + name.Length + 1));
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write(Encoding.ASCII.GetBytes("FPAC"));
        writer.Write(1u);
        writer.Write(checked((uint)offset));
        writer.Write(0u);
        writer.Write(entry.Hash);
        writer.Write(48UL);
        writer.Write(checked((ulong)model.Length));
        writer.Write(offset);
        writer.Write(name);
        writer.Write((byte)0);
        writer.Write(model);
    }

    private static void CheckLooseAndRenamedSources(MainWindow window, string sourceGame,
        CharacterRecord adult, PacArchive sourceArchive, string workspace)
    {
        Console.WriteLine("STAGE loose-only, renamed PAC and loose override fixtures");
        var fixture = Path.Combine(workspace, "loose-source-fixture-" + Guid.NewGuid().ToString("N"));
        var sourceModel = sourceArchive.ReadEntry(adult.ModelEntry);
        var sourceImages = PacArchive.Load(Path.Combine(sourceGame, "pac", "steam", "image.pac"));
        var baseTextureName = "asset/dx11/image/chr5002_01_a.dds";
        var replacementTextureName = "asset/dx11/image/chr5002_02_a.dds";
        if (!sourceImages.TryGet(baseTextureName, out var baseTexture)
            || !sourceImages.TryGet(replacementTextureName, out var replacementTexture))
            throw new InvalidDataException("Real-game material regression textures were not found");
        var replacementBytes = sourceImages.ReadEntry(replacementTexture);
        var previewCache = Path.Combine(outputRootForPreview(), "cache", "models", "Second", "chr5002", "width");
        var replacementPng = Path.Combine(previewCache, "chr5002_02_a.png");
        if (!File.Exists(replacementPng))
            throw new InvalidDataException("The reference material was not rendered by the real-game preview");
        var expectedTextureHash = Hash(replacementPng);
        var gamePath = (TextBox)window.FindName("GamePathBox");
        var selector = (ComboBox)window.FindName("CharacterBox");
        bool Ready() => (bool)typeof(MainWindow).GetField("_modelReady", Private)!.GetValue(window)!;
        foreach (var layout in new[] { "loose", "renamed", "overlay" })
        {
            var root = Path.Combine(fixture, layout);
            Directory.CreateDirectory(root);
            var fixtureExe = Path.Combine(root, "sora_2nd.exe");
            File.Copy(Path.Combine(sourceGame, "sora_2nd.exe"), fixtureExe);
            if (layout == "loose")
            {
                // A changed hash simulates a different game build while keeping a valid x64 PE.
                using var modifiedExe = new FileStream(fixtureExe, FileMode.Append, FileAccess.Write);
                modifiedExe.WriteByte(0);
            }
            var loose = Path.Combine(root, "asset", "common", "model", "chr5002.mdl");
            if (layout is "loose" or "overlay")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
                File.WriteAllBytes(loose, sourceModel);
            }
            if (layout == "renamed")
                WriteSingleModelPac(Path.Combine(root, "pac", "steam", "asset_common_model.pac.disabled"),
                    adult.ModelEntry, sourceModel);
            if (layout == "overlay")
                WriteSingleModelPac(Path.Combine(root, "pac", "steam", "asset_common_model.pac"),
                    adult.ModelEntry, Encoding.ASCII.GetBytes("invalid PAC model; loose file must win"));
            if (layout is "loose" or "overlay")
            {
                var looseTexture = Path.Combine(root, baseTextureName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(looseTexture)!);
                File.WriteAllBytes(looseTexture, replacementBytes);
            }
            if (layout == "overlay")
                WriteSingleModelPac(Path.Combine(root, "pac", "steam", "image.pac"),
                    baseTexture, sourceImages.ReadEntry(baseTexture));

            gamePath.Text = root;
            var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", Private)!.Invoke(window, null)!;
            Pump(() => scan.IsCompleted, layout + " scan");
            scan.GetAwaiter().GetResult();
            var records = selector.Items.Cast<CharacterRecord>().ToList();
            if (records.Count != 1 || records[0].ModelId != "chr5002")
                throw new InvalidDataException($"{layout} scan did not find the isolated character model");
            if ((layout == "renamed") == (records[0].ModelEntry.LoosePath is not null))
                throw new InvalidDataException($"{layout} selected the wrong model source");
            selector.SelectedItem = records[0];
            Pump(Ready, layout + " preview");
            if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
                throw new InvalidDataException($"{layout} preview has no geometry");
            if (layout is "loose" or "overlay")
            {
                var overlaidPng = Path.Combine(previewCache, "chr5002_01_a.png");
                if (!File.Exists(overlaidPng) || Hash(overlaidPng) != expectedTextureHash)
                    throw new InvalidDataException($"{layout} preview did not render the loose texture override");
            }
            var archive = (PacArchive)typeof(MainWindow).GetField("_modelArchive", Private)!.GetValue(window)!;
            if (!archive.ReadEntry(records[0].ModelEntry).SequenceEqual(sourceModel))
                throw new InvalidDataException($"{layout} read a different model than the preview");
            if (layout == "loose")
            {
                GameInstaller.ValidateInstallTarget(root);
                var exeHash = Hash(fixtureExe);
                var originalModelHash = Hash(loose);
                ((Slider)window.FindName("ShapeSlider")).Value = 173;
                var install = (Task)typeof(MainWindow).GetMethod("InstallCurrentAsync", Private)!.Invoke(window, null)!;
                Pump(() => install.IsCompleted, "different-hash 2nd install");
                install.GetAwaiter().GetResult();
                var exportedModel = Path.Combine(outputRootForPreview(), "exports", "second",
                    "chr5002_width_173", "asset", "common", "model", "chr5002.mdl");
                var installedLoader = Path.Combine(root, "xinput1_4.dll");
                if (!File.Exists(exportedModel) || !File.Exists(installedLoader)
                    || Hash(loose) != Hash(exportedModel) || Hash(fixtureExe) != exeHash)
                    throw new InvalidDataException("The different-hash 2nd build did not install the generated model");
                GameInstaller.RestoreLatest(root, Path.Combine(outputRootForPreview(), "install-backups"), () => false);
                if (File.Exists(installedLoader) || Hash(loose) != originalModelHash || Hash(fixtureExe) != exeHash)
                    throw new InvalidDataException("The different-hash 2nd installation was not restored");
            }
        }
    }

    private static void CheckEstelleCostumeInstall(MainWindow window, string sourceGame,
        CharacterRecord costume, PacArchive sourceArchive, string workspace)
    {
        Console.WriteLine("STAGE isolated Estelle 2nd outfit install and restore");
        var root = Path.Combine(workspace, "estelle-costume-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "sora_2nd.exe");
        File.Copy(Path.Combine(sourceGame, "sora_2nd.exe"), exe);
        var exeHash = Hash(exe);
        var model = Path.Combine(root, "asset", "common", "model", costume.ModelFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(model)!);
        File.WriteAllBytes(model, sourceArchive.ReadEntry(costume.ModelEntry));
        var originalHash = Hash(model);

        ((TextBox)window.FindName("GamePathBox")).Text = root;
        var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", Private)!.Invoke(window, null)!;
        Pump(() => scan.IsCompleted, "isolated Estelle costume scan");
        scan.GetAwaiter().GetResult();
        var selector = (ComboBox)window.FindName("CharacterBox");
        var selected = selector.Items.Cast<CharacterRecord>().Single();
        if (selected.ModelId != costume.ModelId || selected.ModelEntry.LoosePath is null)
            throw new InvalidDataException("The isolated 2nd outfit was not selected from its own MDL");
        selector.SelectedItem = selected;
        bool Ready() => (bool)typeof(MainWindow).GetField("_modelReady", Private)!.GetValue(window)!;
        Pump(Ready, "isolated Estelle costume preview");
        ((Slider)window.FindName("ShapeSlider")).Value = 173;
        var install = (Task)typeof(MainWindow).GetMethod("InstallCurrentAsync", Private)!.Invoke(window, null)!;
        Pump(() => install.IsCompleted, "isolated Estelle costume install");
        install.GetAwaiter().GetResult();
        var outputRoot = outputRootForPreview();
        var exported = Path.Combine(outputRoot, "exports", "second", costume.ModelId + "_width_173",
            "asset", "common", "model", costume.ModelFileName);
        var loader = Path.Combine(root, "xinput1_4.dll");
        if (!File.Exists(exported) || !File.Exists(loader) || Hash(model) != Hash(exported)
            || Hash(model) == originalHash || Hash(exe) != exeHash)
            throw new InvalidDataException("The Estelle 2nd outfit was not installed under its own model ID");
        GameInstaller.RestoreLatest(root, Path.Combine(outputRoot, "install-backups"), () => false);
        if (File.Exists(loader) || Hash(model) != originalHash || Hash(exe) != exeHash)
            throw new InvalidDataException("The Estelle 2nd outfit was not restored to the original model");
    }

    [STAThread]
    private static void Main()
    {
        var game = Environment.GetEnvironmentVariable("SKY2ND_GAME_ROOT")
            ?? throw new InvalidOperationException("Set SKY2ND_GAME_ROOT to the installed game directory");
        var exe = Path.Combine(game, "sora_2nd.exe");
        var pac = Path.Combine(game, "pac", "steam", "asset_common_model.pac");
        var imagePac = Path.Combine(game, "pac", "steam", "image.pac");
        var exeHash = Hash(exe);
        var pacLength = new FileInfo(pac).Length;
        var pacWriteTime = File.GetLastWriteTimeUtc(pac);
        var imageLength = new FileInfo(imagePac).Length;
        var imageWriteTime = File.GetLastWriteTimeUtc(imagePac);
        var proxy = Path.Combine(game, "xinput1_4.dll");
        var proxyExisted = File.Exists(proxy);
        var discovered = (string?)typeof(MainWindow).GetMethod("FindDefaultGameRoot", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null);
        if (string.IsNullOrWhiteSpace(discovered) || !Directory.Exists(discovered))
            throw new InvalidDataException("Steam library autodiscovery failed");

        Console.WriteLine("STAGE scan 2nd Chapter");
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        ((TextBox)window.FindName("GamePathBox")).Text = game;
        var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", Private)!.Invoke(window, null)!;
        Pump(() => scan.IsCompleted, "scan");
        scan.GetAwaiter().GetResult();
        var records = ((ComboBox)window.FindName("CharacterBox")).Items.Cast<CharacterRecord>().ToList();
        if (records.Count != 470 || records.Any(record => record.Edition != GameEdition.Second))
            throw new InvalidDataException($"Expected 169 base and 301 costume models; found {records.Count}");

        var child = records.Single(record => record.ModelId == "chr5344");
        var childWithAge = records.Single(record => record.ModelId == "chr5345");
        var bride = records.Single(record => record.ModelId == "chr5710");
        var aliases = records.Single(record => record.ModelId == "chr5500");
        if (child.AgeInfo.Status != "minor" || child.NameSourceKey != "second.name.scene"
            || !child.DisplayName.Contains("童年模型", StringComparison.Ordinal)
            || childWithAge.AgeInfo.Age != 11 || bride.AgeInfo.Status != "unverified"
            || bride.NameSourceKey != "second.name.definition" || aliases.NameAliasCount < 4)
            throw new InvalidDataException("2nd Chapter scene labels, shared-model aliases or age evidence was misclassified");

        var adult = records.Single(record => record.ModelId == "chr5002");
        var adultCostume = records.Single(record => record.ModelId == "chr5002_c01");
        var minorCostume = records.Single(record => record.ModelId == "chr5000_c01");
        var unknown = records.Single(record => record.ModelId == "chr5000");
        var estelleSecond = records.Single(record => record.ModelId == "chr5000_c11");
        if (!adultCostume.AdultShapeEligible || adultCostume.AgeDefinitionLabel is null
            || minorCostume.AdultShapeEligible || minorCostume.AgeDefinitionLabel is null
            || CharacterAgeCatalog.Get("chr5002_c01", true, GameEdition.Second).IsAdult
            || CharacterAgeCatalog.Get("chr5002_c01", true, GameEdition.Second,
                "其他角色：泡澡服").IsAdult)
            throw new InvalidDataException("2nd costume age inheritance ignored the game's definition or admitted an unverified identity");
        if (!unknown.DisplayName.Contains("1st版服装", StringComparison.Ordinal)
            || !estelleSecond.DisplayName.Contains("2nd版轻装服装", StringComparison.Ordinal)
            || estelleSecond.AdultShapeEligible)
            throw new InvalidDataException("Estelle's original and 2nd Chapter outfit resources were not distinguished");
        var previousLanguage = UiText.Current;
        try
        {
            UiText.SetLanguage(UiLanguage.English, persist: false);
            if (!unknown.LocalizedName.Contains("1st outfit", StringComparison.Ordinal)
                || !estelleSecond.LocalizedName.Contains("2nd outfit", StringComparison.Ordinal))
                throw new InvalidDataException("English outfit labels hide the 1st/2nd model distinction");
        }
        finally { UiText.SetLanguage(previousLanguage, persist: false); }
        var archive = (PacArchive)typeof(MainWindow).GetField("_modelArchive", Private)!.GetValue(window)!;
        var modelHash = Convert.ToHexString(SHA256.HashData(archive.ReadEntry(adult.ModelEntry)));
        if (!adult.AdultShapeEligible || unknown.AdultShapeEligible)
            throw new InvalidDataException("Edition-specific age eligibility mismatch");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = adultCostume;
        Pump(Ready, "adult costume preview");
        if (!((ComboBoxItem)window.FindName("ChestModeItem")).IsEnabled)
            throw new InvalidDataException("Adult costume preview did not enable chest editing");
        var costumeExport = ExportService.ExportAsync(adultCostume, archive,
            outputRootForPreview(), 137, false, CancellationToken.None, "chest", false);
        Pump(() => costumeExport.IsCompleted, "adult costume chest export");
        var costumeModel = costumeExport.GetAwaiter().GetResult().ModelPath;
        if (!File.Exists(costumeModel) || new FileInfo(costumeModel).Length != adultCostume.ModelEntry.Size
            || File.ReadAllBytes(costumeModel).SequenceEqual(archive.ReadEntry(adultCostume.ModelEntry)))
            throw new InvalidDataException("Adult costume chest export failed or did not change the model");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = minorCostume;
        Pump(Ready, "minor costume preview");
        if (((ComboBoxItem)window.FindName("ChestModeItem")).IsEnabled)
            throw new InvalidDataException("Minor costume preview enabled chest editing");
        GameInstaller.ValidateInstallTarget(game);

        bool Ready() => (bool)typeof(MainWindow).GetField("_modelReady", Private)!.GetValue(window)!;
        var legacy = records.Single(record => record.ModelId == "chr5003");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = legacy;
        Console.WriteLine("STAGE preview MDL v4 and materials");
        Pump(Ready, "v4 preview");
        if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
            throw new InvalidDataException("No MDL v4 preview geometry");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = estelleSecond;
        Console.WriteLine("STAGE preview Estelle 2nd Chapter costume model");
        Pump(Ready, "Estelle 2nd outfit preview");
        if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
            throw new InvalidDataException("Estelle 2nd outfit has no preview geometry");
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5000_c11", 0);
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = unknown;
        Pump(Ready, "Estelle 1st outfit preview");
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5000", 0);
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = adult;
        Console.WriteLine("STAGE preview MDL v5 and loading panel");
        if (((FrameworkElement)window.FindName("PreviewLoadingPanel")).Visibility != Visibility.Visible
            || ((ProgressBar)window.FindName("PreviewProgressBar")).Visibility != Visibility.Visible)
            throw new InvalidDataException("Model load did not show a working state");
        Pump(Ready, "preview");
        if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
            throw new InvalidDataException("No live model geometry");
        if (((FrameworkElement)window.FindName("PreviewLoadingPanel")).Visibility != Visibility.Collapsed)
            throw new InvalidDataException("Loading panel covered the completed model");
        if (((CheckBox)window.FindName("SummonTestingBox")).IsEnabled)
            throw new InvalidDataException("1st Chapter F8 testing remains enabled for 2nd");

        Console.WriteLine("STAGE regression preview for child hair and reused bridal parts");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = child;
        Pump(Ready, "child bind-pose preview");
        var childMeshes = ((LiveModelView)window.FindName("LiveView")).SourceMeshes;
        var hair = childMeshes.Single(mesh => mesh.name.Contains("Mesh_hair_chr5347", StringComparison.Ordinal));
        var face = childMeshes.First(mesh => mesh.name.Contains("chr5344_face", StringComparison.Ordinal)
            && mesh.name.EndsWith("_0", StringComparison.Ordinal));
        var hairMin = hair.positions.Min(vertex => vertex[1]);
        var hairMax = hair.positions.Max(vertex => vertex[1]);
        var faceMax = face.positions.Max(vertex => vertex[1]);
        if (hairMin >= faceMax - .08 || hairMax <= faceMax - .05 || hairMax >= faceMax + .15)
            throw new InvalidDataException($"Child hair does not overlap the face after bind alignment: {hairMin:F3}..{hairMax:F3} / {faceMax:F3}");
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5344", 0);
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5344", -478);
        var childMetaPath = Path.Combine(outputRootForPreview(), "cache", "models", "Second", "chr5344", "width", "model-meta.json");
        using (var childMeta = System.Text.Json.JsonDocument.Parse(File.ReadAllText(childMetaPath)))
        {
            if (!childMeta.RootElement.GetProperty("borrowed_model_ids").EnumerateArray()
                    .Any(value => value.GetString() == "chr5347"))
                throw new InvalidDataException("Borrowed child hair was not disclosed in model evidence");
        }
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = bride;
        Pump(Ready, "bridal resource preview");
        if (!((TextBlock)window.FindName("SelectedMetaText")).Text.Contains("chr5500", StringComparison.Ordinal))
            throw new InvalidDataException("Reused bridal hair and face were not disclosed");
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5710", 0);
        CapturePreview((LiveModelView)window.FindName("LiveView"), "chr5710", -478);

        Console.WriteLine("STAGE export without changing the game");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = adult;
        Pump(Ready, "restore adult preview");
        ((Slider)window.FindName("ShapeSlider")).Value = 100;
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "SkyCharacterStudio.csproj")))
            project = project.Parent;
        var outputRoot = project?.FullName ?? AppContext.BaseDirectory;
        var export = ExportService.ExportAsync(adult, archive, outputRoot, 100, false,
            CancellationToken.None, "width", false);
        Pump(() => export.IsCompleted, "offline export");
        var exported = export.GetAwaiter().GetResult();
        var model = Path.Combine(outputRoot, "exports", "second", "chr5002_width_100", "asset", "common", "model", "chr5002.mdl");
        if (!File.Exists(model) || File.ReadAllBytes(model)[..4] is not [77, 68, 76, 32])
            throw new InvalidDataException("MDL v5 offline export missing or malformed");
        if (File.Exists(proxy) != proxyExisted || Hash(exe) != exeHash
            || new FileInfo(pac).Length != pacLength || File.GetLastWriteTimeUtc(pac) != pacWriteTime
            || new FileInfo(imagePac).Length != imageLength || File.GetLastWriteTimeUtc(imagePac) != imageWriteTime
            || Convert.ToHexString(SHA256.HashData(archive.ReadEntry(adult.ModelEntry))) != modelHash)
            throw new InvalidDataException("A checked game file changed during offline export");
        if (!exported.ModelPath.Equals(model, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Offline export reported the wrong model path");
        CheckLooseAndRenamedSources(window, game, adult, archive,
            Environment.GetEnvironmentVariable("SKY1ST_SMOKE_ROOT") ?? outputRoot);
        CheckEstelleCostumeInstall(window, game, estelleSecond, archive,
            Environment.GetEnvironmentVariable("SKY1ST_SMOKE_ROOT") ?? outputRoot);
        Console.WriteLine("PASS scan 169 base plus 301 costume models; Estelle 2nd outfit preview; v4/v5 preview; loading state; per-game age gate; local export; executable hash, selected model hash, PAC metadata and proxy state unchanged");
        app.Shutdown();
    }

    private static string outputRootForPreview()
    {
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "SkyCharacterStudio.csproj")))
            project = project.Parent;
        return project?.FullName ?? AppContext.BaseDirectory;
    }

    private static void CapturePreview(LiveModelView view, string modelId, int strength)
    {
        var directory = Environment.GetEnvironmentVariable("SKY2ND_PREVIEW_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        view.SetStrength(strength);
        view.Frame(true);
        view.Measure(new Size(1000, 800));
        view.Arrange(new Rect(0, 0, 1000, 800));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"{modelId}_{strength}.png"));
        encoder.Save(file);
    }
}
