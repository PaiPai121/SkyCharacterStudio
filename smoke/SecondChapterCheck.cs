using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
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
        if (records.Count != 170 || records.Any(record => record.Edition != GameEdition.Second))
            throw new InvalidDataException($"Expected 170 2nd Chapter characters; found {records.Count}");

        var adult = records.Single(record => record.ModelId == "chr5002");
        var unknown = records.Single(record => record.ModelId == "chr5000");
        var archive = (PacArchive)typeof(MainWindow).GetField("_modelArchive", Private)!.GetValue(window)!;
        var modelHash = Convert.ToHexString(SHA256.HashData(archive.ReadEntry(adult.ModelEntry)));
        if (!adult.AdultShapeEligible || unknown.AdultShapeEligible)
            throw new InvalidDataException("Edition-specific age eligibility mismatch");
        try
        {
            GameInstaller.ValidateSupportedGame(game);
            throw new InvalidDataException("Unverified 2nd Chapter installation was enabled");
        }
        catch (InvalidOperationException) { }

        bool Ready() => (bool)typeof(MainWindow).GetField("_modelReady", Private)!.GetValue(window)!;
        var legacy = records.Single(record => record.ModelId == "chr5003");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = legacy;
        Console.WriteLine("STAGE preview MDL v4 and materials");
        Pump(Ready, "v4 preview");
        if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
            throw new InvalidDataException("No MDL v4 preview geometry");
        ((ComboBox)window.FindName("CharacterBox")).SelectedItem = adult;
        Console.WriteLine("STAGE preview MDL v5, material and portrait");
        Pump(Ready, "preview");
        if (((LiveModelView)window.FindName("LiveView")).Geometry.Count == 0)
            throw new InvalidDataException("No live model geometry");
        if (adult.PreviewEntry is not null && ((Image)window.FindName("PortraitImage")).Source is null)
            throw new InvalidDataException("2nd Chapter LZ4 portrait not rendered");
        if (((CheckBox)window.FindName("SummonTestingBox")).IsEnabled)
            throw new InvalidDataException("1st Chapter F8 testing remains enabled for 2nd");

        Console.WriteLine("STAGE export without changing the game");
        ((Slider)window.FindName("ShapeSlider")).Value = 100;
        var export = (Task)typeof(MainWindow).GetMethod("InstallCurrentAsync", Private)!.Invoke(window, null)!;
        Pump(() => export.IsCompleted, "offline export");
        export.GetAwaiter().GetResult();
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "Sky1stCharacterStudio.csproj")))
            project = project.Parent;
        var outputRoot = project?.FullName ?? AppContext.BaseDirectory;
        var model = Path.Combine(outputRoot, "exports", "second", "chr5002_width_100", "asset", "common", "model", "chr5002.mdl");
        if (!File.Exists(model) || File.ReadAllBytes(model)[..4] is not [77, 68, 76, 32])
            throw new InvalidDataException("MDL v5 offline export missing or malformed");
        if (File.Exists(proxy) != proxyExisted || Hash(exe) != exeHash
            || new FileInfo(pac).Length != pacLength || File.GetLastWriteTimeUtc(pac) != pacWriteTime
            || new FileInfo(imagePac).Length != imageLength || File.GetLastWriteTimeUtc(imagePac) != imageWriteTime
            || Convert.ToHexString(SHA256.HashData(archive.ReadEntry(adult.ModelEntry))) != modelHash)
            throw new InvalidDataException("A checked game file changed during offline export");
        var status = ((TextBlock)window.FindName("StatusText")).Text;
        if (!status.Contains(model, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Offline export path not shown to the user");
        Console.WriteLine("PASS scan 170; v4/v5 preview, portrait, per-game age gate; local export; installation blocked; executable hash, selected model hash, PAC metadata and proxy state unchanged");
        app.Shutdown();
    }
}
