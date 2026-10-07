using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class FullAdultCheck {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Pump(Func<bool> done) {var deadline=DateTime.UtcNow.AddSeconds(90);while(!done()){if(DateTime.UtcNow>deadline)throw new Exception("Preview timeout");var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(10);}}
 [STAThread] static void Main() {
  UiText.SetLanguage(UiLanguage.Chinese, persist:false);
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var w=new MainWindow();var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",Flags)!.Invoke(w,null)!;Pump(()=>scan.IsCompleted);scan.GetAwaiter().GetResult();
  bool Ready()=>(bool)typeof(MainWindow).GetField("_modelReady",Flags)!.GetValue(w)!;
  var box=(ComboBox)w.FindName("CharacterBox");var modes=(ComboBox)w.FindName("ShapeModeBox");var chest=(ComboBoxItem)w.FindName("ChestModeItem");var view=(LiveModelView)w.FindName("LiveView");
  Pump(Ready);
  foreach(var id in box.Items.Cast<CharacterRecord>().Where(r=>r.AdultShapeEligible && !r.AgeInfo.DefaultedFromBaseGame).Select(r=>r.ModelId).OrderBy(id=>id).ToArray()) {
   box.SelectedItem=box.Items.Cast<CharacterRecord>().Single(r=>r.ModelId==id);Pump(Ready);
   var r=(CharacterRecord)box.SelectedItem;var details=((TextBlock)w.FindName("SelectedMetaText")).Text;
   if(!details.Contains(r.AgeInfo.DisplayText))throw new Exception("Missing age status "+id);
   bool supported=true;
   if(chest.IsEnabled!=supported)throw new Exception("Wrong chest capability "+id);
   if(supported) {
    modes.SelectedItem=chest;Pump(Ready);
    foreach(var strength in new[]{-500,0,100,1000}) {
     ((Slider)w.FindName("ShapeSlider")).Value=strength;
     var raw=File.ReadAllBytes($@"D:\work_console\Sky1stCharacterStudio\cache\full-adult-check\{id}\chest\{strength}\{id}.mdl");int off=12,start=0;
     while(off+8<=raw.Length){if(BitConverter.ToInt32(raw,off)==4)start=off+8;off+=8+BitConverter.ToInt32(raw,off+4);}
     double max=0;
     for(int m=0;m<view.Geometry.Count;m++)for(int i=0;i<view.Geometry[m].Positions.Count;i++) {var p=view.Geometry[m].Positions[i];int o=start+view.SourceMeshes[m].positionOffset+i*12;max=Math.Max(max,Math.Abs(p.X-BitConverter.ToSingle(raw,o)));max=Math.Max(max,Math.Abs(p.Y-BitConverter.ToSingle(raw,o+4)));max=Math.Max(max,Math.Abs(p.Z-BitConverter.ToSingle(raw,o+8)));}
     if(max>1e-6)throw new Exception($"Preview/export mismatch {id} {strength}: {max}");
     if(strength!=0)for(int mesh=0;mesh<view.Geometry.Count;mesh++) {
      var src=view.SourceMeshes[mesh];if(src.normalOffset<0)continue;
      for(int vertex=0;vertex<view.Geometry[mesh].Normals.Count;vertex++) {
       var normal=view.Geometry[mesh].Normals[vertex];int offset=start+src.normalOffset+vertex*4;
       double error=Math.Max(Math.Abs(normal.X-unchecked((sbyte)raw[offset])/127.0),Math.Max(Math.Abs(normal.Y-unchecked((sbyte)raw[offset+1])/127.0),Math.Abs(normal.Z-unchecked((sbyte)raw[offset+2])/127.0)));
       if(error>.004)throw new Exception("Preview/export normal mismatch "+error);
      }
     }
     if(strength==-500 || strength==0 || strength==1000) {
      foreach(var yaw in new[]{0.0,1.2}) {
       typeof(LiveModelView).GetField("angle",Flags)!.SetValue(view,yaw);
       view.Frame(false);view.Measure(new Size(240,300));view.Arrange(new Rect(0,0,240,300));view.UpdateLayout();
       var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(240,300,96,96,System.Windows.Media.PixelFormats.Pbgra32);bmp.Render(view);
       var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
       using var f=File.Create($@"D:\work_console\Sky1stCharacterStudio\cache\full-adult-check\{id}\preview-{strength}-{yaw}.png");enc.Save(f);
      }
     }
    }
    modes.SelectedIndex=0;Pump(Ready);
   }
   Console.WriteLine($"PASS WPF selection, age status, capability and preview/export: {id} {r.AgeInfo.DisplayText}");
  }
  var defaultedAdult=new CharacterRecord{ModelId="chr9999",IsBaseGameCharacter=true};
  if(!defaultedAdult.AdultShapeEligible || defaultedAdult.AgeInfo.Catalogued || defaultedAdult.AgeInfo.DisplayText!="年龄目录未登记 · 默认成年")throw new Exception("Unlisted model is not treated as a defaulted adult");
  var estelle=new CharacterRecord{ModelId="chr5000",IsBaseGameCharacter=true};if(estelle.AdultShapeEligible || estelle.AgeInfo.Age!=16)throw new Exception("Known minor incorrectly eligible");
  Console.WriteLine("PASS known-minor restriction and unlisted-model default-adult policy; all UI checks ran offscreen without controlling the user desktop");app.Shutdown();
 }
}
