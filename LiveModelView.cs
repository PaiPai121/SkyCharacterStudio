using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Sky1stCharacterStudio;

public sealed class LiveMesh
{
    public string name { get; set; } = "";
    public bool hidden { get; set; }
    public bool opaque { get; set; }
    public double[][] positions { get; set; } = [];
    public double[][] adjusted { get; set; } = [];
    public double[][] normals { get; set; } = [];
    public double[][] normalDelta { get; set; } = [];
    public int normalOffset { get; set; } = -1;
    public double[][] uv { get; set; } = [];
    public int[] indices { get; set; } = [];
    public string texture { get; set; } = "";
    public int positionOffset { get; set; }
}

public sealed class LiveModelView : Grid
{
    public List<LiveMesh> SourceMeshes { get; private set; } = [];
    public List<MeshGeometry3D> Geometry { get; } = [];
    private readonly Viewport3D viewport = new();
    private readonly Model3DGroup scene = new();
    private readonly PerspectiveCamera camera = new() { FieldOfView = 35, NearPlaneDistance=.01, FarPlaneDistance=30 };
    private double angle = .65, pitch = .1, distance = 1.6;
    private Point last;
    private double targetHeight = 1.25;
    private double modelMinY=0, modelHeight=1.8, modelWidth=1.5;
    public LiveModelView()
    {
        Background = new SolidColorBrush(Color.FromRgb(35,44,55));
        Children.Add(viewport);
        viewport.Camera = camera;
        viewport.Children.Add(new ModelVisual3D { Content=scene });
        MouseLeftButtonDown += (_,e) => { last=e.GetPosition(this); CaptureMouse(); };
        MouseLeftButtonUp += (_,_) => ReleaseMouseCapture();
        MouseMove += (_,e) => { if (!IsMouseCaptured) return; var p=e.GetPosition(this); angle-=(p.X-last.X)*.01; pitch=Math.Clamp(pitch+(p.Y-last.Y)*.005,-1,1); last=p; UpdateCamera(); };
        MouseWheel += (_,e) => { distance=Math.Clamp(distance*Math.Exp(-e.Delta*.001),.45,6); UpdateCamera(); };
        UpdateCamera();
    }
    public void Frame(bool full) { targetHeight=modelMinY+modelHeight*(full ? .5 : .75); distance=full ? Math.Max(modelHeight*1.9,modelWidth*1.9) : modelHeight*.9; UpdateCamera(); }
    private void UpdateCamera()
    {
        var offset=new Vector3D(Math.Sin(angle)*Math.Cos(pitch)*distance,Math.Sin(pitch)*distance,Math.Cos(angle)*Math.Cos(pitch)*distance);
        camera.Position=new Point3D(offset.X,targetHeight+offset.Y,offset.Z);
        camera.LookDirection=-offset; camera.UpDirection=new Vector3D(0,1,0);
    }
    public void Load(string directory, string fileName = "chr5002.json")
    {
        SourceMeshes=JsonSerializer.Deserialize<List<LiveMesh>>(File.ReadAllText(Path.Combine(directory,fileName)))!;
        var positions=SourceMeshes.Where(m=>!m.hidden).SelectMany(m=>m.positions).ToArray();
        if(positions.Length==0) throw new InvalidDataException("模型没有可显示的网格");
        modelMinY=positions.Min(p=>p[1]);modelHeight=positions.Max(p=>p[1])-modelMinY;
        modelWidth=positions.Max(p=>p[0])-positions.Min(p=>p[0]);
        Geometry.Clear(); scene.Children.Clear();
        scene.Children.Add(new AmbientLight(Color.FromRgb(125,125,125)));
        scene.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-1,-2)));
        foreach(var mesh in SourceMeshes)
        {
            var geo=new MeshGeometry3D { TriangleIndices=new Int32Collection(mesh.indices), Normals=new Vector3DCollection(mesh.normals.Select(n=>new Vector3D(n[0],n[1],n[2]))), TextureCoordinates=new PointCollection(mesh.uv.Select(u=>new Point(u[0],u[1]))) };
            Geometry.Add(geo);
            Brush brush=Brushes.LightGray;
            if(mesh.texture.Length>0) {
                BitmapSource texture=new BitmapImage(new Uri(Path.GetFullPath(Path.Combine(directory,mesh.texture))));
                // These game materials use alpha as shader data, not transparency.
                if(mesh.opaque) texture=new FormatConvertedBitmap(texture,PixelFormats.Bgr32,null,0);
                texture.Freeze();
                brush=new ImageBrush(texture) { ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,1,1), TileMode=TileMode.Tile };
            }
            var material=new DiffuseMaterial(brush);
            if(!mesh.hidden) scene.Children.Add(new GeometryModel3D(geo,material) { BackMaterial=material });
        }
        SetStrength(0);
    }
    private static double ChestStrength(double strength)
    {
        var t = Math.Clamp(Math.Round(strength), -500, 1000) / 100d;
        if (t > 1) return 1 + (t - 1) * .60;
        if (t < -1) return -1 + (t + 1) * .60;
        return t;
    }

    public void SetStrength(double strength, bool chestMode = false)
    {
        double t=chestMode ? ChestStrength(strength) : Math.Clamp(Math.Round(strength),-500,1000)/100d;
        for(int m=0;m<SourceMeshes.Count;m++)
        {
            var source=SourceMeshes[m];
            var points=new Point3DCollection(source.positions.Length);
            var normals=new Vector3DCollection(source.positions.Length);
            for(int i=0;i<source.positions.Length;i++) {
                var a=source.positions[i]; var b=source.adjusted[i];
                points.Add(new Point3D(a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1]),a[2]+t*(b[2]-a[2])));
                var n=source.normals[i];var normal=new Vector3D(n[0],n[1],n[2]);
                if(source.normalDelta.Length==source.positions.Length) {
                    var d=source.normalDelta[i];
                    double aa=1+t*d[0],bb=t*d[1],cc=t*d[2],dd=t*d[3],ee=1+t*d[4],ff=t*d[5],gg=t*d[6],hh=t*d[7],ii=1+t*d[8];
                    normal=new Vector3D((ee*ii-ff*hh)*n[0]+(ff*gg-dd*ii)*n[1]+(dd*hh-ee*gg)*n[2],
                        (cc*hh-bb*ii)*n[0]+(aa*ii-cc*gg)*n[1]+(bb*gg-aa*hh)*n[2],
                        (bb*ff-cc*ee)*n[0]+(cc*dd-aa*ff)*n[1]+(aa*ee-bb*dd)*n[2]);
                }
                if(normal.LengthSquared>1e-20)normal.Normalize();
                normals.Add(normal);
            }
            points.Freeze(); Geometry[m].Positions=points;
            normals.Freeze();Geometry[m].Normals=normals;
        }
    }
}
