using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class AgeCheck {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Pump(Func<bool> done) {var deadline=DateTime.UtcNow.AddSeconds(90);while(!done()){if(DateTime.UtcNow>deadline)throw new Exception("Preview timeout");var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(10);}}
 [STAThread] static void Main() {
  UiText.SetLanguage(UiLanguage.Chinese, persist:false);
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var w=new MainWindow();var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",Flags)!.Invoke(w,null)!;Pump(()=>scan.IsCompleted);scan.GetAwaiter().GetResult();
  bool Ready()=>(bool)typeof(MainWindow).GetField("_modelReady",Flags)!.GetValue(w)!;
  var box=(ComboBox)w.FindName("CharacterBox");var modes=(ComboBox)w.FindName("ShapeModeBox");var chest=(ComboBoxItem)w.FindName("ChestModeItem");var view=(LiveModelView)w.FindName("LiveView");
  if(box.Items.Cast<CharacterRecord>().Any(r=>!r.IsBaseGameCharacter))throw new Exception("Scanner provenance missing");
  Pump(Ready);
  foreach(var id in new[]{"chr5102","chr5111","chr5107","chr5101","chr5004"}) {
   box.SelectedItem=box.Items.Cast<CharacterRecord>().Single(r=>r.ModelId==id);Pump(Ready);
   var r=(CharacterRecord)box.SelectedItem;var details=((TextBlock)w.FindName("SelectedMetaText")).Text;
   if(!details.Contains(r.AgeInfo.DisplayText))throw new Exception("Missing age status "+id);
   bool supported=id is "chr5102" or "chr5111" or "chr5107" or "chr5101";
   if(chest.IsEnabled!=supported)throw new Exception("Wrong chest capability "+id);
   if(supported) {
    modes.SelectedItem=chest;Pump(Ready);
    foreach(var strength in new[]{-500,0,100,1000}) {
     ((Slider)w.FindName("ShapeSlider")).Value=strength;
     var raw=File.ReadAllBytes($@"D:\work_console\Sky1stCharacterStudio\cache\age-check\{id}\{strength}\{id}.mdl");int off=12,start=0;
     while(off+8<=raw.Length){if(BitConverter.ToInt32(raw,off)==4)start=off+8;off+=8+BitConverter.ToInt32(raw,off+4);}
     double max=0;
     for(int m=0;m<view.Geometry.Count;m++)for(int i=0;i<view.Geometry[m].Positions.Count;i++) {var p=view.Geometry[m].Positions[i];int o=start+view.SourceMeshes[m].positionOffset+i*12;max=Math.Max(max,Math.Abs(p.X-BitConverter.ToSingle(raw,o)));max=Math.Max(max,Math.Abs(p.Y-BitConverter.ToSingle(raw,o+4)));max=Math.Max(max,Math.Abs(p.Z-BitConverter.ToSingle(raw,o+8)));}
     if(max>1e-6)throw new Exception("Preview/export mismatch "+max);
     if(id=="chr5107" && (strength==0 || strength==1000)) {
      foreach(var yaw in new[]{0.0,1.2}) {
       typeof(LiveModelView).GetField("angle",Flags)!.SetValue(view,yaw);
       view.Frame(false);view.Measure(new Size(800,800));view.Arrange(new Rect(0,0,800,800));view.UpdateLayout();
       var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(800,800,96,96,System.Windows.Media.PixelFormats.Pbgra32);bmp.Render(view);
       var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
       using var f=File.Create($@"D:\work_console\Sky1stCharacterStudio\cache\age-check\julia-{strength}-{yaw}.png");enc.Save(f);
      }
     }
    }
    modes.SelectedIndex=0;Pump(Ready);
   }
   Console.WriteLine($"PASS WPF selection, age status, capability and preview/export: {id} {r.AgeInfo.DisplayText}");
  }
  var unknown=new CharacterRecord{ModelId="chr9999"};if(unknown.AdultShapeEligible || unknown.AgeInfo.DisplayText!="年龄资料不足")throw new Exception("Unknown misclassified");
  var scannedUnknown=new CharacterRecord{ModelId="chr9999",IsBaseGameCharacter=true};if(scannedUnknown.AdultShapeEligible || scannedUnknown.AgeInfo.Status!="unknown")throw new Exception("Unlisted base-game character incorrectly eligible");
  var estelle=new CharacterRecord{ModelId="chr5000",IsBaseGameCharacter=true};if(estelle.AdultShapeEligible || estelle.AgeInfo.Age!=16)throw new Exception("Known minor incorrectly eligible");
  Console.WriteLine("PASS known-minor and unknown restrictions; all UI checks ran offscreen without controlling the user desktop");app.Shutdown();
 }
}
