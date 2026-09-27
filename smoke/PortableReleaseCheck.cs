using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class PortableReleaseCheck {
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static string FindProjectRoot(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null){if(File.Exists(Path.Combine(directory.FullName,"SkyCharacterStudio.csproj")))return directory.FullName;directory=directory.Parent;}return AppContext.BaseDirectory;}
 static void Pump(Func<bool> done,string stage="operation"){
  var limit=DateTime.UtcNow.AddSeconds(120);var nextProgress=DateTime.UtcNow.AddSeconds(10);
  while(!done()){
   if(DateTime.UtcNow>limit)throw new Exception("Timeout during "+stage);
   if(DateTime.UtcNow>=nextProgress){Console.WriteLine("STAGE "+stage+" still active");nextProgress=DateTime.UtcNow.AddSeconds(10);}
   var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);
  }
 }
 static void WriteSingleModelPac(PacArchive source,PacEntry entry,string path){
  var name=Encoding.UTF8.GetBytes(entry.Name);var model=source.ReadEntry(entry);var offset=checked((ulong)(48+name.Length+1));
  using var writer=new BinaryWriter(File.Create(path),Encoding.UTF8);
  writer.Write(Encoding.ASCII.GetBytes("FPAC"));writer.Write(1u);writer.Write(checked((uint)offset));writer.Write(0u);
  writer.Write(entry.Hash);writer.Write(48UL);writer.Write(checked((ulong)model.Length));writer.Write(offset);
  writer.Write(name);writer.Write((byte)0);writer.Write(model);
 }
 [STAThread] static void Main(){
  var root=FindProjectRoot();var smokeRoot=Environment.GetEnvironmentVariable("SKY1ST_SMOKE_ROOT") ?? root;var game=Environment.GetEnvironmentVariable("SKY1ST_GAME_ROOT") ?? @"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st";
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var w=new MainWindow();((TextBox)w.FindName("GamePathBox")).Text=game;
  if(!((TextBlock)w.FindName("TargetGameText")).Text.Contains("1st",StringComparison.Ordinal))throw new Exception("1st target is not shown");
  var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",F)!.Invoke(w,null)!;Pump(()=>scan.IsCompleted);scan.GetAwaiter().GetResult();
  bool Ready()=>(bool)typeof(MainWindow).GetField("_modelReady",F)!.GetValue(w)!;
  Pump(Ready);var box=(ComboBox)w.FindName("CharacterBox");var r=box.Items.Cast<CharacterRecord>().Single(r=>r.ModelId=="chr5107");box.SelectedItem=r;Pump(Ready);
  if(((CheckBox)w.FindName("SummonTestingBox")).IsChecked==true)throw new Exception("Summon enabled by default");
  if(((LiveModelView)w.FindName("LiveView")).Geometry.Count==0)throw new Exception("No preview geometry");
  var shapeSlider=(Slider)w.FindName("ShapeSlider");var resetButton=(Button)w.FindName("ResetShapeButton");shapeSlider.Value=777;resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  if(Math.Abs(shapeSlider.Value)>1e-9 || !((TextBlock)w.FindName("PreviewStrengthText")).Text.Equals("0%",StringComparison.Ordinal))throw new Exception("Reset shape button failed");
  var archive=PacArchive.Load(Path.Combine(game,"pac/steam/asset_common_model.pac"));
  ExportResult Export(int strength,bool summon){var t=ExportService.ExportAsync(r,archive,root,strength,true,CancellationToken.None,"chest",summon);Pump(()=>t.IsCompleted);return t.GetAwaiter().GetResult();}
  var normal=Export(100,false);if(normal.SummonEnabled || !File.ReadAllText(Path.Combine(normal.RuntimePackagePath!,"ED9Loader/config/EventStarter.ini")).Contains("enabled=0"))throw new Exception("Normal install not disabled");
  var test=Export(101,true);if(!test.SummonEnabled || !File.Exists(Path.Combine(test.RuntimePackagePath!,"Mod/ScherazardSummon/asset/common/model/chr_studio_original.mdl")))throw new Exception("Missing original comparison");
  var fake=Path.Combine(smokeRoot,"test-game");Directory.CreateDirectory(Path.Combine(fake,"pac/steam"));File.Copy(Path.Combine(game,"sora_1st.exe"),Path.Combine(fake,"sora_1st.exe"),true);
  var fakePacPath=Path.Combine(fake,"pac/steam/asset_common_model.pac");WriteSingleModelPac(archive,r.ModelEntry,fakePacPath);
  using(var imageWriter=new BinaryWriter(File.Create(Path.Combine(fake,"pac/steam/image.pac")))){
   imageWriter.Write(Encoding.ASCII.GetBytes("FPAC"));imageWriter.Write(0u);imageWriter.Write(16u);imageWriter.Write(0u);
  }
  GameInstaller.ValidateSupportedGame(fake);
  try { StudioSummonService.ValidateScriptSource(fake);throw new Exception("Missing summon PAC accepted"); }
  catch(FileNotFoundException error) { if(!error.Message.Contains("script_sc.pac") || !error.Message.Contains("F8"))throw; }
  ((TextBox)w.FindName("GamePathBox")).Text=fake;
  if(box.Items.Count!=0 || ((Button)w.FindName("InstallButton")).IsEnabled)throw new Exception("Old game models remained after changing the target");
  var fakeScan=(Task)typeof(MainWindow).GetMethod("ScanAsync",F)!.Invoke(w,null)!;Pump(()=>fakeScan.IsCompleted);fakeScan.GetAwaiter().GetResult();
  box.SelectedItem=box.Items.Cast<CharacterRecord>().Single(item=>item.ModelId=="chr5107");Pump(Ready,"isolated 1st preview");
  ((CheckBox)w.FindName("SummonTestingBox")).IsChecked=true;
  ((ComboBox)w.FindName("ShapeModeBox")).SelectedIndex=0;
  Pump(Ready);
  shapeSlider.Value=102;
  var installTask=(Task)typeof(MainWindow).GetMethod("InstallCurrentAsync",F)!.Invoke(w,null)!;
  Pump(()=>installTask.IsCompleted);installTask.GetAwaiter().GetResult();
  var model=Path.Combine(fake,"Mod/ScherazardSummon/asset/common/model/chr5107.mdl");
  var generated=Path.Combine(root,"exports","chr5107_width_102","asset","common","model","chr5107.mdl");
  if(!File.Exists(generated) || !File.Exists(model) || !File.ReadAllBytes(generated).SequenceEqual(File.ReadAllBytes(model)))throw new Exception("F8 failure blocked normal model generation or installation");
  var status=((TextBlock)w.FindName("StatusText")).Text;
  if(!status.Contains("script_sc.pac") || !status.Contains("F8"))throw new Exception("F8 fallback was not explained in the UI");
  if(!File.ReadAllText(Path.Combine(fake,"ED9Loader/config/EventStarter.ini")).Contains("enabled=0") ||
     !File.ReadAllText(Path.Combine(fake,"Mod/ScherazardSummon/add_dat_ini.json")).Contains("\"inject\":[]"))throw new Exception("F8 fallback left summon enabled");
  GameInstaller.RestoreLatest(fake,Path.Combine(root,"install-backups"),()=>false);
  ((CheckBox)w.FindName("SummonTestingBox")).IsChecked=false;
  ((TextBox)w.FindName("GamePathBox")).Text=game;
  Directory.CreateDirectory(Path.GetDirectoryName(model)!);File.WriteAllBytes(model,[1,2,3]);
  var backups=Path.Combine(smokeRoot,"test-backups");GameInstaller.Install(normal.RuntimePackagePath!,fake,backups,()=>false);
  var installed=File.ReadAllBytes(model);File.WriteAllBytes(model,[8,9]);
  try{GameInstaller.RestoreLatest(fake,backups,()=>false);throw new Exception("Clobbered another mod");}catch(IOException){}
  File.WriteAllBytes(model,installed);GameInstaller.RestoreLatest(fake,backups,()=>false);
  if(!File.ReadAllBytes(model).SequenceEqual(new byte[]{1,2,3}) || File.Exists(Path.Combine(fake,"xinput1_4.dll")))throw new Exception("Restore mismatch");
  File.WriteAllBytes(Path.Combine(fake,"sora_1st.exe"),[1,2,3]);
  try{GameInstaller.Install(normal.RuntimePackagePath!,fake,backups,()=>false);throw new Exception("Unsupported executable accepted");}catch(InvalidOperationException){}
  ((TextBox)w.FindName("GamePathBox")).Text=fake;
  var offlineScan=(Task)typeof(MainWindow).GetMethod("ScanAsync",F)!.Invoke(w,null)!;Pump(()=>offlineScan.IsCompleted);offlineScan.GetAwaiter().GetResult();
  box.SelectedItem=box.Items.Cast<CharacterRecord>().Single(item=>item.ModelId=="chr5107");Pump(Ready);
  ((CheckBox)w.FindName("SummonTestingBox")).IsChecked=true;
  shapeSlider.Value=103;
  var offlineTask=(Task)typeof(MainWindow).GetMethod("InstallCurrentAsync",F)!.Invoke(w,null)!;
  Pump(()=>offlineTask.IsCompleted);offlineTask.GetAwaiter().GetResult();
  var offline=Path.Combine(root,"exports","chr5107_width_103","asset","common","model","chr5107.mdl");
  if(!File.Exists(offline) || !File.ReadAllBytes(model).SequenceEqual(new byte[]{1,2,3}) || File.Exists(Path.Combine(fake,"xinput1_4.dll")) ||
     !((TextBlock)w.FindName("StatusText")).Text.Contains(offline))throw new Exception("Unsupported game did not generate an offline model safely");
  ((TextBox)w.FindName("GamePathBox")).Text="";
  if(((TextBlock)w.FindName("TargetGameText")).Text!=UiText.T("game.target.none") || box.Items.Count!=0)
   throw new Exception("Clearing the game folder left stale target information");
  File.WriteAllText(Path.Combine(smokeRoot,"portable-test-result.txt"),"PASS isolated WPF preview, bundled Python and native builder, normal/summon exports, missing-F8 fallback generated and installed the model, unsupported executable generated offline only, install/restore, changed-file protection. No writes to the actual game.");
  Console.WriteLine("PASS portable release checks");app.Shutdown();
 }
}
