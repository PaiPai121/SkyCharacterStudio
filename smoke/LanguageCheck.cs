using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;

class LanguageCheck
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Language check timed out");
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
        UiText.SetLanguage(UiLanguage.Chinese, persist: false);
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var window = new MainWindow();
        var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", PrivateInstance)!.Invoke(window, null)!;
        Pump(() => scan.IsCompleted);
        scan.GetAwaiter().GetResult();
        Pump(() => (bool)typeof(MainWindow).GetField("_modelReady", PrivateInstance)!.GetValue(window)!);

        var heading = (TextBlock)window.FindName("AppHeadingText");
        var scanButton = (Button)window.FindName("ScanButton");
        var installButton = (Button)window.FindName("InstallButton");
        var chestItem = (ComboBoxItem)window.FindName("ChestModeItem");
        var selectedName = (TextBlock)window.FindName("SelectedNameText");
        var applyLanguage = typeof(MainWindow).GetMethod("ApplyLanguage", PrivateInstance)!;

        if (!heading.Text.Contains("游击士", StringComparison.Ordinal)
            || !Equals(scanButton.Content, "扫描游戏资源")
            || !Equals(installButton.Content, "保存并应用到游戏"))
            throw new Exception("Chinese UI resources were not applied");

        UiText.SetLanguage(UiLanguage.English, persist: false);
        applyLanguage.Invoke(window, null);
        Pump(() => heading.Text.Contains("Character Model Studio", StringComparison.Ordinal));
        if (!Equals(scanButton.Content, "Scan game resources")
            || !Equals(chestItem.Content, "Chest / torso (automatic placement)")
            || !Equals(installButton.Content, "Save & apply to game"))
            throw new Exception("English UI resources were not applied");
        if (selectedName.Text.Contains("雪拉扎德", StringComparison.Ordinal))
            throw new Exception("Selected character name was not localized");

        UiText.SetLanguage(UiLanguage.Chinese, persist: false);
        applyLanguage.Invoke(window, null);
        if (!heading.Text.Contains("游击士", StringComparison.Ordinal)
            || !Equals(scanButton.Content, "扫描游戏资源"))
            throw new Exception("Chinese UI roundtrip failed");

        window.Close();
        app.Shutdown();
        Console.WriteLine($"PASS language roundtrip: {selectedName.Text} / {heading.Text}");
    }
}
