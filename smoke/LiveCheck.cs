using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class LiveCheck {
 [STAThread] static void Main() {
  UiText.SetLanguage(UiLanguage.Chinese, persist:false);
  var app=new Application();
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var window=new MainWindow();
  var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
  while(!scan.IsCompleted) { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
  scan.GetAwaiter().GetResult();
  var view=(LiveModelView)window.FindName("LiveView");
  var slider=(Slider)window.FindName("ShapeSlider");
  if(view.Visibility!=Visibility.Visible||view.Geometry.Count!=10) throw new Exception("Scan did not load real model");
  foreach(var strength in new[]{-500,-100,0,97,100,300,1000}) {
   slider.Value=strength;
   var data=File.ReadAllBytes($@"D:\work_console\Sky1stCharacterStudio\exports\chr5002_shape_{strength:000}\asset\common\model\chr5002.mdl");
   int off=12, start=0;
   while(off+8<=data.Length) { if(BitConverter.ToInt32(data,off)==4) start=off+8; off+=8+BitConverter.ToInt32(data,off+4); }
   double error=0; int count=0;
   for(int m=0;m<view.Geometry.Count;m++) for(int i=0;i<view.Geometry[m].Positions.Count;i++) {
    var p=view.Geometry[m].Positions[i]; var o=start+view.SourceMeshes[m].positionOffset+12*i;
    error=Math.Max(error,Math.Abs(p.X-BitConverter.ToSingle(data,o)));
    error=Math.Max(error,Math.Abs(p.Y-BitConverter.ToSingle(data,o+4)));
    error=Math.Max(error,Math.Abs(p.Z-BitConverter.ToSingle(data,o+8))); count++;
   }
   if(error>1e-6) throw new Exception("Live/export vertex mismatch");
   Console.WriteLine($"{strength}% live/export: {count} vertices, max error {error}");
  }
  var toggle=(Button)window.FindName("PreviewToggleButton"); toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  if(!toggle.Content.ToString()!.Contains("返回")) throw new Exception("Original toggle failed");
  toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  slider.Value=0;
  typeof(LiveModelView).GetField("angle",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(view,-1.3);
  view.Frame(false);
  window.Width=1240;window.Height=780;
  window.Measure(new Size(1240,780));window.Arrange(new Rect(0,0,1240,780)); window.UpdateLayout();
  var visual=(FrameworkElement)window.Content;
  window.Content=null;
  visual.Resources=window.Resources;
  visual.Measure(new Size(1240,780));visual.Arrange(new Rect(0,0,1240,780));visual.UpdateLayout();
  var bitmap=new RenderTargetBitmap(1240,780,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
  using(var f=File.Create(@"D:\work_console\Sky1stCharacterStudio\cache\live-window.png")) png.Save(f);
  Console.WriteLine("Rendered actual WPF window at 1240x780; toggle roundtrip passed");
  app.Shutdown();
 }
}
