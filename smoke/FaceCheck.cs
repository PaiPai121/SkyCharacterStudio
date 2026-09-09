using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sky1stCharacterStudio;
class FaceCheck {
 [STAThread] static void Main() {
  var app=new Application();
  var view=new LiveModelView();
  view.Load(@"D:\work_console\Sky1stCharacterStudio\assets\live");
  foreach(var angle in new[]{0.0,1.1}) {
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   typeof(LiveModelView).GetField("angle",flags)!.SetValue(view,angle);
   typeof(LiveModelView).GetField("pitch",flags)!.SetValue(view,0.0);
   typeof(LiveModelView).GetField("targetHeight",flags)!.SetValue(view,1.57);
   typeof(LiveModelView).GetField("distance",flags)!.SetValue(view,.7);
   typeof(LiveModelView).GetMethod("UpdateCamera",flags)!.Invoke(view,null);
   view.Measure(new Size(700,700));view.Arrange(new Rect(0,0,700,700));view.UpdateLayout();
   var bmp=new RenderTargetBitmap(700,700,96,96,PixelFormats.Pbgra32);bmp.Render(view);
   var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));
   using var f=File.Create($@"D:\work_console\Sky1stCharacterStudio\cache\face-{angle}.png");enc.Save(f);
  }
  app.Shutdown();
 }
}
