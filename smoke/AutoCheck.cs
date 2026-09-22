using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class AutoCheck {
 static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Pump(Func<bool> done) {var deadline=DateTime.UtcNow.AddSeconds(60);while(!done()){if(DateTime.UtcNow>deadline)throw new Exception("Preview timeout");var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(10);}}
 [STAThread] static void Main() {
  UiText.SetLanguage(UiLanguage.Chinese, persist:false);
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var window=new MainWindow();var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",flags)!.Invoke(window,null)!;Pump(()=>scan.IsCompleted);scan.GetAwaiter().GetResult();
  var box=(ComboBox)window.FindName("CharacterBox");var view=(LiveModelView)window.FindName("LiveView");
  Pump(()=> (bool)typeof(MainWindow).GetField("_modelReady",flags)!.GetValue(window)!);
  foreach(var id in new[]{"chr5000","chr5004","chr5101","chr5300"}) {
   box.SelectedItem=box.Items.Cast<CharacterRecord>().Single(r=>r.ModelId==id);
   Pump(()=> (bool)typeof(MainWindow).GetField("_modelReady",flags)!.GetValue(window)!);
   if(view.SourceMeshes.Count==0)throw new Exception("Empty model");
   var details=((TextBlock)window.FindName("SelectedMetaText")).Text;
   if(!details.Contains("骨骼检测：") || !details.Contains("资格："))throw new Exception("Missing visible capability details");
   if(id=="chr5004" && !details.Contains("已确认未成年"))throw new Exception("Age status incorrectly reported");
   foreach(var strength in new[]{-500,0,100,1000}) {
    ((Slider)window.FindName("ShapeSlider")).Value=strength;
    var raw=File.ReadAllBytes($@"D:\work_console\Sky1stCharacterStudio\cache\auto-check\{id}\width\{strength}\{id}.mdl");int off=12,start=0;
    while(off+8<=raw.Length){if(BitConverter.ToInt32(raw,off)==4)start=off+8;off+=8+BitConverter.ToInt32(raw,off+4);}
    double max=0;
    for(int m=0;m<view.Geometry.Count;m++)for(int i=0;i<view.Geometry[m].Positions.Count;i++) {var p=view.Geometry[m].Positions[i];int o=start+view.SourceMeshes[m].positionOffset+i*12;max=Math.Max(max,Math.Abs(p.X-BitConverter.ToSingle(raw,o)));max=Math.Max(max,Math.Abs(p.Y-BitConverter.ToSingle(raw,o+4)));max=Math.Max(max,Math.Abs(p.Z-BitConverter.ToSingle(raw,o+8)));}
    if(max>1e-6)throw new Exception("Preview/export mismatch "+max);
   }
   view.SetStrength(0);view.Frame(true);view.Measure(new Size(800,800));view.Arrange(new Rect(0,0,800,800));view.UpdateLayout();var bmp=new RenderTargetBitmap(800,800,96,96,PixelFormats.Pbgra32);bmp.Render(view);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create($@"D:\work_console\Sky1stCharacterStudio\cache\auto-{id}.png"))enc.Save(f);
   Console.WriteLine($"PASS actual selection + slider + exported vertices + WPF render: {id}");
  }
  app.Shutdown();
 }
}
