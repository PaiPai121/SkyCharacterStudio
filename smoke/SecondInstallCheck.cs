using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;

internal static class SecondInstallCheck
{
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteSingleModelPac(PacArchive source, PacEntry entry, string path)
    {
        var name = Encoding.UTF8.GetBytes(entry.Name);
        var model = source.ReadEntry(entry);
        var payloadOffset = checked((ulong)(48 + name.Length + 1));
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write(Encoding.ASCII.GetBytes("FPAC"));
        writer.Write(1u);
        writer.Write(checked((uint)payloadOffset));
        writer.Write(0u);
        writer.Write(entry.Hash);
        writer.Write(48UL);
        writer.Write(checked((ulong)model.Length));
        writer.Write(payloadOffset);
        writer.Write(name);
        writer.Write((byte)0);
        writer.Write(model);
    }

    private static void Pump(Func<bool> done, string stage)
    {
        var start = DateTime.UtcNow;
        while (!done())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromMinutes(4))
                throw new TimeoutException($"{stage} exceeded four minutes");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    [STAThread]
    private static void Main()
    {
        UiText.Initialize();
        var sourceGame = Environment.GetEnvironmentVariable("SKY2ND_GAME_ROOT")
            ?? throw new InvalidOperationException("Set SKY2ND_GAME_ROOT");
        var loader = Path.Combine(AppContext.BaseDirectory, "runtime", "second-loader", "xinput1_4.dll");
        var workspace = Environment.GetEnvironmentVariable("SKY1ST_SMOKE_ROOT")
            ?? throw new InvalidOperationException("Set SKY1ST_SMOKE_ROOT");
        var loaderHash = SecondLoaderService.Validate(loader);
        var sourceArchive = PacArchive.Load(Path.Combine(sourceGame, "pac", "steam", "asset_common_model.pac"));
        var record = CharacterScanner.Build(sourceArchive, null, null).Single(item => item.ModelId == "chr5002");
        var testRoot = Path.Combine(workspace, "second-install-fixture-" + Guid.NewGuid().ToString("N"));
        var fakeGame = Path.Combine(testRoot, "游戏 2nd");
        Directory.CreateDirectory(Path.Combine(fakeGame, "pac", "steam"));
        var fakeExe = Path.Combine(fakeGame, "sora_2nd.exe");
        File.Copy(Path.Combine(sourceGame, "sora_2nd.exe"), fakeExe);
        using (var changedBuild = new FileStream(fakeExe, FileMode.Append, FileAccess.Write))
            changedBuild.WriteByte(0);
        var fakePac = Path.Combine(fakeGame, "pac", "steam", "asset_common_model.pac");
        WriteSingleModelPac(sourceArchive, record.ModelEntry, fakePac);
        using (var imageWriter = new BinaryWriter(File.Create(Path.Combine(fakeGame, "pac", "steam", "image.pac"))))
        {
            imageWriter.Write(Encoding.ASCII.GetBytes("FPAC"));
            imageWriter.Write(0u);
            imageWriter.Write(16u);
            imageWriter.Write(0u);
        }
        var exeHash = Hash(fakeExe);
        var pacHash = Hash(fakePac);
        GameInstaller.ValidateInstallTarget(fakeGame);

        Console.WriteLine("STAGE export 2nd runtime package");
        var result = ExportService.ExportAsync(record, sourceArchive, testRoot, 100, true,
            CancellationToken.None, "width", false, loader).GetAwaiter().GetResult();
        var package = result.RuntimePackagePath ?? throw new InvalidDataException("Missing 2nd runtime package");
        var packageFiles = Directory.GetFiles(package, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(package, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!packageFiles.SequenceEqual(new[] { "asset/common/model/chr5002.mdl", "xinput1_4.dll" }))
            throw new InvalidDataException("2nd runtime package contains unexpected files: " + string.Join(", ", packageFiles));
        if (Hash(Path.Combine(package, "xinput1_4.dll")) != loaderHash
            || Hash(Path.Combine(package, "asset", "common", "model", "chr5002.mdl")) != Hash(result.ModelPath))
            throw new InvalidDataException("Package bytes differ from the sources");

        var backupRoot = Path.Combine(testRoot, "backups");
        var targetLoader = Path.Combine(fakeGame, "xinput1_4.dll");
        var targetModel = Path.Combine(fakeGame, "asset", "common", "model", "chr5002.mdl");
        Console.WriteLine("STAGE install and restore in isolated game copy");
        var backup = GameInstaller.Install(package, fakeGame, backupRoot, () => false);
        if (!File.Exists(Path.Combine(backup, "installation.json"))
            || Hash(targetLoader) != loaderHash || Hash(targetModel) != Hash(result.ModelPath)
            || Hash(fakeExe) != exeHash || Hash(fakePac) != pacHash
            || Directory.Exists(Path.Combine(fakeGame, "ED9Loader")))
            throw new InvalidDataException("2nd isolated install changed unexpected files or copied wrong bytes");
        File.WriteAllText(targetLoader, "another proxy");
        try
        {
            GameInstaller.Install(package, fakeGame, backupRoot, () => false);
            throw new Exception("A conflicting loader was overwritten");
        }
        catch (IOException) { }
        if (File.ReadAllText(targetLoader) != "another proxy")
            throw new InvalidDataException("A conflicting loader was changed");
        File.Copy(Path.Combine(package, "xinput1_4.dll"), targetLoader, true);
        GameInstaller.RestoreLatest(fakeGame, backupRoot, () => false);
        if (File.Exists(targetLoader) || File.Exists(targetModel)
            || Hash(fakeExe) != exeHash || Hash(fakePac) != pacHash)
            throw new InvalidDataException("2nd restore did not undo the installation cleanly");

        var checks = 0;
        try
        {
            GameInstaller.Install(package, fakeGame, backupRoot, () => ++checks >= 3);
            throw new Exception("Interrupted installation was accepted");
        }
        catch (IOException) { }
        if (File.Exists(targetLoader) || File.Exists(targetModel))
            throw new InvalidDataException("Interrupted installation was not rolled back");
        File.WriteAllText(fakeExe, "unsupported executable");
        try
        {
            GameInstaller.Install(package, fakeGame, backupRoot, () => false);
            throw new Exception("Malformed executable was accepted");
        }
        catch (InvalidOperationException) { }
        if (Hash(fakePac) != pacHash)
            throw new InvalidDataException("PAC was modified");
        File.Copy(Path.Combine(sourceGame, "sora_2nd.exe"), fakeExe, true);
        using (var changedBuild = new FileStream(fakeExe, FileMode.Append, FileAccess.Write))
            changedBuild.WriteByte(0);
        if (Hash(fakeExe) != exeHash)
            throw new InvalidDataException("The alternate executable hash fixture changed unexpectedly");

        Console.WriteLine("STAGE WPF Save & apply to isolated 2nd copy");
        UiText.SetLanguage(UiLanguage.Chinese, persist: false);
        var project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project is not null && !File.Exists(Path.Combine(project.FullName, "SkyCharacterStudio.csproj")))
            project = project.Parent;
        var projectRoot = project?.FullName ?? AppContext.BaseDirectory;
        if (!string.Equals(SecondLoaderService.FindAvailable(fakeGame, projectRoot), loader,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The studio did not select its bundled 2nd Chapter loader");
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        ((TextBox)window.FindName("GamePathBox")).Text = fakeGame;
        if (!((TextBlock)window.FindName("TargetGameText")).Text.Contains("2nd", StringComparison.Ordinal))
            throw new InvalidDataException("The selected 2nd Chapter target is not shown");
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", privateInstance)!.Invoke(window, null)!;
        Pump(() => scan.IsCompleted, "2nd UI scan");
        scan.GetAwaiter().GetResult();
        bool Ready() => (bool)typeof(MainWindow).GetField("_modelReady", privateInstance)!.GetValue(window)!;
        Pump(Ready, "2nd UI preview");
        ((Slider)window.FindName("ShapeSlider")).Value = 100;
        var uiInstall = (Task)typeof(MainWindow).GetMethod("InstallCurrentAsync", privateInstance)!.Invoke(window, null)!;
        Pump(() => uiInstall.IsCompleted, "2nd UI install");
        uiInstall.GetAwaiter().GetResult();
        if (!File.Exists(targetLoader) || !File.Exists(targetModel)
            || Hash(targetLoader) != loaderHash
            || !((TextBlock)window.FindName("StatusText")).Text.Contains("已将", StringComparison.Ordinal))
            throw new InvalidDataException("The 2nd UI did not install or report success");
        GameInstaller.RestoreLatest(fakeGame, Path.Combine(projectRoot, "install-backups"), () => false);
        if (File.Exists(targetLoader) || File.Exists(targetModel)
            || Hash(fakeExe) != exeHash || Hash(fakePac) != pacHash)
            throw new InvalidDataException("The 2nd UI installation did not restore cleanly");
        ((TextBox)window.FindName("GamePathBox")).Text = Path.Combine(testRoot, "not-a-game");
        if (((TextBlock)window.FindName("TargetGameText")).Text != UiText.T("game.target.unknown")
            || ((ComboBox)window.FindName("CharacterBox")).Items.Count != 0
            || ((Button)window.FindName("InstallButton")).IsEnabled)
            throw new InvalidDataException("Changing the game folder retained the old target or models");
        app.Shutdown();
        Console.WriteLine("PASS 2nd package layout, model bytes, proxy identity, different-hash install, malformed-executable gate, conflict guard, rollback, restore and WPF install path; no real game files written");
    }
}
