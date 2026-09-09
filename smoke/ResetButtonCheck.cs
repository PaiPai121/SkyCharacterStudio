using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;

class ResetButtonCheck
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Preview timeout");
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
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        var scan = (Task)typeof(MainWindow).GetMethod("ScanAsync", Flags)!.Invoke(window, null)!;
        Pump(() => scan.IsCompleted);
        scan.GetAwaiter().GetResult();
        Pump(() => (bool)typeof(MainWindow).GetField("_modelReady", Flags)!.GetValue(window)!);

        var slider = (Slider)window.FindName("ShapeSlider");
        var reset = (Button)window.FindName("ResetShapeButton");
        var contour = (ShapePreviewControl)window.FindName("ContourPreview");
        if (!reset.IsEnabled) throw new Exception("Reset button is disabled after model load");

        slider.Value = 1000;
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Math.Abs(slider.Value) > 1e-9 || Math.Abs(contour.Strength) > 1e-9)
            throw new Exception("Reset button did not return slider and contour preview to 0%");
        if (!((TextBlock)window.FindName("PreviewStrengthText")).Text.Equals("0%", StringComparison.Ordinal))
            throw new Exception("Reset button did not refresh visible strength");

        Console.WriteLine("PASS reset shape button: strength, contour preview and visible label returned to 0%");
        app.Shutdown();
    }
}
