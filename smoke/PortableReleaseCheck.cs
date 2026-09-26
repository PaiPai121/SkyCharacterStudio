using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sky1stCharacterStudio;
class PortableReleaseCheck {
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static string FindProjectRoot(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null){if(File.Exists(Path.Combine(directory.FullName,"Sky1stCharacterStudio.csproj")))return directory.FullName;directory=directory.Parent;}return AppContext.BaseDirectory;}
 static void Pump(Func<bool> done){var limit=DateTime.UtcNow.AddSeconds(120);while(!done()){if(DateTime.UtcNow>limit)throw new Exception("Timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
 [STAThread] static void Main(){
  var root=FindProjectRoot();var smokeRoot=Environment.GetEnvironmentVariable("SKY1ST_SMOKE_ROOT") ?? root;var game=Environment.GetEnvironmentVariable("SKY1ST_GAME_ROOT") ?? @"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st";
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var w=new MainWindow();((TextBox)w.FindName("GamePathBox")).Text=game;
  var scan=(Task)typeof(MainWindow).GetMethod("ScanAsync",F)!.Invoke(w,null)!;Pump(()=>scan.IsCompleted);scan.GetAwaiter().GetResult();
  bool Ready()=>(bool)typeof(MainWindow).GetField("_modelReady",F)!.GetValue(w)!;
  Pump(Ready);var box=(ComboBox)w.FindName("CharacterBox");var r=box.Items.Cast<CharacterRecord>().Single(r=>r.ModelId=="chr5107");box.SelectedItem=r;Pump(Ready);
  if(((CheckBox)w.FindName("SummonTestingBox")).IsChecked==true)throw new Exception("Summon enabled by default");
  if(((LiveModelView)w.FindName("LiveView")).Geometry.Count==0)throw new Exception("No preview geometry");
  var shapeSlider=(Slider)w.FindName("ShapeSlider");var resetButton=(Button)w.FindName("ResetShapeButton");shapeSlider.Value=777;resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  if(Math.Abs(shapeSlider.Value)>1e-9 || !((TextBlock)w.FindName("PreviewStrengthText")).Text.Equals("0%",StringComparison.Ordinal))throw new Exception("Reset shape button failed");
  var archive=PacArchive.Load(Path.Combine(game,"pac/steam/asset_common_model.pac"));
  ExportResult Export(int strength,bool summon){var t=ExportService.ExportAsync(r,archive,root,strength,true,CancellationToken.None,"chest",summon);Pump(()=>t.IsCompleted);return t.GetAwaiter().GetResult();}
  var normal=Export(100,false);if(!File.ReadAllText(Path.Combine(normal.RuntimePackagePath!,"ED9Loader/config/EventStarter.ini")).Contains("enabled=0"))throw new Exception("Normal install not disabled");
  var test=Export(101,true);if(!File.Exists(Path.Combine(test.RuntimePackagePath!,"Mod/ScherazardSummon/asset/common/model/chr_studio_original.mdl")))throw new Exception("Missing original comparison");
  var fake=Path.Combine(smokeRoot,"test-game");Directory.CreateDirectory(Path.Combine(fake,"pac/steam"));File.Copy(Path.Combine(game,"sora_1st.exe"),Path.Combine(fake,"sora_1st.exe"),true);File.WriteAllBytes(Path.Combine(fake,"pac/steam/asset_common_model.pac"),[1]);
  GameInstaller.ValidateSupportedGame(fake);
  try { StudioSummonService.ValidateScriptSource(fake);throw new Exception("Missing summon PAC accepted"); }
  catch(FileNotFoundException error) { if(!error.Message.Contains("script_sc.pac") || !error.Message.Contains("F8"))throw; }
  ((TextBox)w.FindName("GamePathBox")).Text=fake;
  ((CheckBox)w.FindName("SummonTestingBox")).IsChecked=true;
  var installTask=(Task)typeof(MainWindow).GetMethod("InstallCurrentAsync",F)!.Invoke(w,null)!;
  Pump(()=>installTask.IsCompleted);installTask.GetAwaiter().GetResult();
  if(!((TextBlock)w.FindName("StatusText")).Text.Contains("script_sc.pac"))throw new Exception("Missing PAC was not explained in the UI");
  ((CheckBox)w.FindName("SummonTestingBox")).IsChecked=false;
  ((TextBox)w.FindName("GamePathBox")).Text=game;
  var model=Path.Combine(fake,"Mod/ScherazardSummon/asset/common/model/chr5107.mdl");Directory.CreateDirectory(Path.GetDirectoryName(model)!);File.WriteAllBytes(model,[1,2,3]);
  var backups=Path.Combine(smokeRoot,"test-backups");GameInstaller.Install(normal.RuntimePackagePath!,fake,backups,()=>false);
  var installed=File.ReadAllBytes(model);File.WriteAllBytes(model,[8,9]);
  try{GameInstaller.RestoreLatest(fake,backups,()=>false);throw new Exception("Clobbered another mod");}catch(IOException){}
  File.WriteAllBytes(model,installed);GameInstaller.RestoreLatest(fake,backups,()=>false);
  if(!File.ReadAllBytes(model).SequenceEqual(new byte[]{1,2,3}) || File.Exists(Path.Combine(fake,"xinput1_4.dll")))throw new Exception("Restore mismatch");
  File.WriteAllBytes(Path.Combine(fake,"sora_1st.exe"),[1,2,3]);
  try{GameInstaller.Install(normal.RuntimePackagePath!,fake,backups,()=>false);throw new Exception("Unsupported executable accepted");}catch(InvalidOperationException){}
  File.WriteAllText(Path.Combine(smokeRoot,"portable-test-result.txt"),"PASS isolated WPF preview, bundled Python and native builder, normal/summon exports, actionable missing-summon-PAC error, install/restore, changed-file protection, executable compatibility guard. No writes to the actual game.");
  Console.WriteLine("PASS portable release checks");app.Shutdown();
 }
}
