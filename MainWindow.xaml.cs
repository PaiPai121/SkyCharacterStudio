using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Sky1stCharacterStudio;

public partial class MainWindow : Window
{
    private readonly string _projectRoot;
    private readonly PreviewService _previewService;
    private readonly ObservableCollection<CharacterRecord> _visibleCharacters = new();
    private List<CharacterRecord> _allCharacters = new();
    private PacArchive? _modelArchive;
    private PacArchive? _imageArchive;
    private CharacterRecord? _selectedCharacter;
    private int _previewGeneration;
    private bool _isScanning;
    private bool _showAdjustedPreview;

    public MainWindow()
    {
        InitializeComponent();
        _projectRoot = ResolveProjectRoot();
        _previewService = new PreviewService(_projectRoot);
        GamePathBox.Text = FindDefaultGameRoot();
        var savedPath = Path.Combine(_projectRoot, "game-directory.txt");
        if (File.Exists(savedPath)) GamePathBox.Text = File.ReadAllText(savedPath).Trim();
        CharacterBox.ItemsSource = _visibleCharacters;
        ContourPreview.Strength = ShapeSlider.Value;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await ScanAsync();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_isScanning) return;
        _isScanning = true;
        SetActionState(false);
        var root = GamePathBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root))
        {
            SetStatus("请先选择游戏目录。", true);
            SetActionState(true);
            _isScanning = false;
            return;
        }

        var steamRoot = Path.Combine(root, "pac", "steam");
        var modelPath = Path.Combine(steamRoot, "asset_common_model.pac");
        var infoPath = Path.Combine(steamRoot, "asset_common_model_info.pac");
        var imagePath = Path.Combine(steamRoot, "image.pac");
        SetStatus("正在读取 FPAC 索引（只读，不会抽取整个大包）…");

        try
        {
            var result = await Task.Run(() =>
            {
                var model = PacArchive.Load(modelPath);
                PacArchive? modelInfo = File.Exists(infoPath) ? PacArchive.Load(infoPath) : null;
                PacArchive? image = File.Exists(imagePath) ? PacArchive.Load(imagePath) : null;
                var characters = CharacterScanner.Build(model, modelInfo, image);
                return (model, image, characters);
            });
            _modelArchive = result.model;
            _imageArchive = result.image;
            _allCharacters = result.characters;
            File.WriteAllText(Path.Combine(_projectRoot,"game-directory.txt"), root);
            ApplyFilter(selectPreferred: true);
            var portraitCount = _allCharacters.Count(x => x.PreviewEntry is not null);
            SetStatus($"扫描完成：{_allCharacters.Count} 个基础角色模型，{portraitCount} 个找到头像贴图。原始 PAC 保持只读。", false);
        }
        catch (Exception exception)
        {
            _modelArchive = null;
            _imageArchive = null;
            _allCharacters.Clear();
            _visibleCharacters.Clear();
            _selectedCharacter = null;
            CharacterBox.SelectedItem = null;
            SetStatus($"扫描失败：{exception.Message}。请确认目录下存在 pac\\steam\\asset_common_model.pac。", true);
        }
        finally
        {
            SetActionState(true);
            _isScanning = false;
        }
    }

    private void ApplyFilter(bool selectPreferred = false)
    {
        var filter = FilterBox.Text.Trim();
        var previousId = _selectedCharacter?.ModelId;
        var matches = string.IsNullOrWhiteSpace(filter)
            ? _allCharacters
            : _allCharacters.Where(character =>
                character.ModelId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || character.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        _visibleCharacters.Clear();
        foreach (var record in matches) _visibleCharacters.Add(record);

        CharacterRecord? preferred = null;
        if (previousId is not null) preferred = _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals(previousId, StringComparison.OrdinalIgnoreCase));
        if (preferred is null && selectPreferred)
            preferred = _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals("chr5002", StringComparison.OrdinalIgnoreCase))
                ?? _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals("chr5000", StringComparison.OrdinalIgnoreCase));
        CharacterBox.SelectedItem = preferred ?? _visibleCharacters.FirstOrDefault();
        ModelCountText.Text = _allCharacters.Count == 0 ? "未扫描" : $"{matches.Count}/{_allCharacters.Count}";
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allCharacters.Count > 0) ApplyFilter();
    }

    private bool _modelReady;
    private string _materialWarning="";
    private string CurrentMode => (ShapeModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "width";
    private async void CharacterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ++_previewGeneration; _modelReady=false; LiveView.Visibility=Visibility.Collapsed;
        if(CharacterBox.SelectedItem is not CharacterRecord record) {_selectedCharacter=null; return;}
        _selectedCharacter=record; _showAdjustedPreview=true;
        ChestModeItem.IsEnabled=false;
        ChestModeItem.ToolTip="正在检测骨骼；成年资格单独检查";
        if(ShapeModeBox.SelectedIndex<0 || !record.AdultShapeEligible && CurrentMode=="chest") {
            ShapeModeBox.SelectedIndex=0; // SelectionChanged initiates the load.
            return;
        }
        await LoadSelectedModelAsync();
    }
    private async void ShapeMode_Changed(object sender, SelectionChangedEventArgs e) {
        if(_selectedCharacter!=null) await LoadSelectedModelAsync();
    }
    private async Task LoadSelectedModelAsync() {
        var record=_selectedCharacter;if(record==null)return;
        int generation=++_previewGeneration;var mode=CurrentMode;
        _modelReady=false; LiveView.Visibility=Visibility.Collapsed;
        PortraitImage.Source=null; SetActionState(false); UpdateCharacterDetails(record);
        PortraitStatusText.Text=$"正在自动提取 {record.DisplayName} 的模型和贴图…";
        var directory=Path.Combine(_projectRoot,"cache","models",record.ModelId,mode);
        try {
            await AutoModelService.Run(GamePathBox.Text,record.ModelId,mode,directory);
            if(generation!=_previewGeneration)return;
            LiveView.Load(directory,"model.json"); LiveView.Frame(true);
            using(var meta=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"model-meta.json")))) {
                int missing=meta.RootElement.GetProperty("missing_textures").GetArrayLength();
                _materialWarning=missing>0 ? $" 缺少 {missing} 张贴图，对应区域显示灰色。" : "";
                var bones=meta.RootElement.GetProperty("chest_detection");
                var recognized=bones.TryGetProperty("can_deform",out var capability) && capability.GetBoolean();
                var eligibility=record.AgeInfo.DisplayText;
                if (meta.RootElement.GetProperty("adult_eligible").GetBoolean()!=record.AdultShapeEligible)
                    throw new InvalidDataException("年龄资料版本不一致，请重新启动工作台");
                var boneStatus="骨骼检测："+bones.GetProperty("detail").GetString();
                var boneNames=bones.TryGetProperty("bones",out var boneList)
                    ? string.Join("、",boneList.EnumerateArray().Select(b=>b.GetString())) : "";
                SelectedMetaText.Text+="\n\n"+boneStatus+(boneNames.Length>0 ? "\n"+boneNames : "")+"\n资格："+eligibility;

                ChestModeItem.IsEnabled=record.AdultShapeEligible && recognized;
                ChestModeItem.ToolTip=boneStatus+"；"+eligibility+"\n"+record.AgeInfo.Basis+"\n"+record.AgeInfo.Source;
                SelectedMetaText.ToolTip=record.AgeInfo.Basis+"\n"+record.AgeInfo.Source;
                _materialWarning+=" "+boneStatus+"；"+eligibility+"。";
                if (mode=="chest" && meta.RootElement.GetProperty("deformation_gain").GetDouble()<.999)
                    _materialWarning+=" 已按模型约束局部位移，降低极限强度翻面的风险。";

            }
            _modelReady=true; LiveView.Visibility=Visibility.Visible;RefreshLive();
        } catch(Exception error) {
            if(generation==_previewGeneration) PortraitStatusText.Text="无法加载此调整方式："+error.Message;
        } finally {if(generation==_previewGeneration)SetActionState(true);}
    }

    private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _showAdjustedPreview=!_showAdjustedPreview;
        RefreshLive();
    }
    private void FullBody_Click(object sender, RoutedEventArgs e) => LiveView.Frame(true);
    private void UpperBody_Click(object sender, RoutedEventArgs e) => LiveView.Frame(false);
    private void RefreshLive()
    {
        if(LiveView is null || ShapeSlider is null || !_modelReady) return;
        var strength=(int)Math.Round(ShapeSlider.Value);
        LiveView.SetStrength(_showAdjustedPreview ? strength : 0);
        PreviewToggleButton.Content=_showAdjustedPreview ? "切换原版" : "返回当前调整";
        PortraitStatusText.Text=_showAdjustedPreview
            ? $"实时预览：{strength}% · 导出使用同一强度。{(CurrentMode=="chest" ? "胸部／胸廓自动定位" : "整体宽度")}；光照以游戏内为准。"
            : $"正在对照原版（0%）；返回当前调整可查看 {strength}%。";
        PortraitStatusText.Text+=_materialWarning;
    }

    private void UpdateCharacterDetails(CharacterRecord record)
    {
        SelectedNameText.Text = record.DisplayName;
        SelectedIdText.Text = record.ModelId;
        SelectedMetaText.Text = $"模型大小：{record.ArchiveSizeText}\n"
            + $"模型信息：{(record.ModelInfoEntry is null ? "未找到" : "已找到")}\n"
            + $"头像：{record.PreviewText}\n{record.SupportText}";
        ContourPreview.ModelLabel = record.ModelId.Equals("chr5002", StringComparison.OrdinalIgnoreCase)
            ? "雪拉扎德轮廓"
            : $"{record.ModelId} 轮廓";
        PreviewToggleButton.IsEnabled = record.IsSupportedShapeEdit;
        PreviewToggleButton.Content = _showAdjustedPreview ? "显示原版 3D 预览" : "查看调整后 3D 对照";
    }

    private void ShapeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var strength = Math.Clamp(e.NewValue, -500, 1000);
        PreviewStrengthText.Text = $"{strength:0}%";
        ContourPreview.Strength = strength;
        _showAdjustedPreview=true;
        RefreshLive();
    }

    private void ResetShapeButton_Click(object sender, RoutedEventArgs e)
    {
        ShapeSlider.Value = 0;
    }


    private async void InstallButton_Click(object sender, RoutedEventArgs e) => await InstallCurrentAsync();

    private async Task InstallCurrentAsync()
    {
        try {
            var target = GameInstaller.ValidateGameRoot(GamePathBox.Text);
            if (GameInstaller.IsGameRunning()) throw new InvalidOperationException("游戏正在运行，请先退出游戏再安装。");
            var expected = Path.Combine(target,"pac","steam","asset_common_model.pac");
            if (_modelArchive is null || !Path.GetFullPath(_modelArchive.Path).Equals(expected,StringComparison.OrdinalIgnoreCase))
                await ScanAsync();
            if (_modelArchive is null || !Path.GetFullPath(_modelArchive.Path).Equals(expected,StringComparison.OrdinalIgnoreCase)
                || _selectedCharacter is null || !_modelReady)
                throw new InvalidOperationException("请等待所选模型加载成功。");
            SetActionState(false);
            var strength=(int)Math.Round(ShapeSlider.Value);
            SetStatus($"正在生成 {strength}% 模型并安装到：{target}");
            var testSummon=SummonTestingBox.IsChecked==true;
            var result=await ExportService.ExportAsync(_selectedCharacter,_modelArchive,_projectRoot,strength,true,CancellationToken.None,CurrentMode,testSummon);
            if(!result.ShapeEditApplied || result.RuntimePackagePath is null) throw new InvalidOperationException("模型生成失败，未安装。");
            var backup=await Task.Run(()=>GameInstaller.Install(result.RuntimePackagePath,target,Path.Combine(_projectRoot,"install-backups")));
            SetStatus($"已安装 {strength}% 模型到：{target}\n"+(testSummon ? "F8 召唤本次角色；F9 切换版本后再按 F8 刷新。" : "角色正常出场时生效；测试召唤已关闭。")+$"备份：{backup}");
        } catch(Exception error) { SetStatus(error.Message,true); }
        finally { SetActionState(true); }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择游戏目录（包含 sora_1st.exe 的文件夹）",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        try {
            GamePathBox.Text = GameInstaller.ValidateGameRoot(dialog.FolderName);
            await ScanAsync();
        } catch(Exception error) { SetStatus(error.Message,true); }
    }

    private async void RestoreButton_Click(object sender,RoutedEventArgs e) {
        try {
            SetActionState(false);
            var target=GameInstaller.ValidateGameRoot(GamePathBox.Text);
            var restored=await Task.Run(()=>GameInstaller.RestoreLatest(target,Path.Combine(_projectRoot,"install-backups")));
            SetStatus("已撤销上次安装："+restored);
        } catch(Exception error) {SetStatus(error.Message,true);}
        finally {SetActionState(true);}
    }

    private void SetActionState(bool enabled)
    {
        ScanButton_ClickEnabled(enabled);
        if(RestoreButton is not null)RestoreButton.IsEnabled=enabled;
        if(ResetShapeButton is not null)ResetShapeButton.IsEnabled=enabled && _modelReady;
        if(SummonTestingBox is not null)SummonTestingBox.IsEnabled=enabled;
        BrowseButton.IsEnabled = enabled;
        GamePathBox.IsEnabled = enabled;
        InstallButton.IsEnabled = enabled && _modelReady;
        ShapeModeBox.IsEnabled = enabled || !_modelReady;
        FilterBox.IsEnabled = enabled;
        CharacterBox.IsEnabled = enabled && _allCharacters.Count > 0;
        ShapeSlider.IsEnabled = enabled && _modelReady;
        PreviewToggleButton.IsEnabled = enabled && _modelReady;
    }

    private void ScanButton_ClickEnabled(bool enabled) => ScanButton.IsEnabled = enabled;

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            error ? System.Windows.Media.Color.FromRgb(236, 157, 137) : System.Windows.Media.Color.FromRgb(145, 172, 182));
    }

    private static string ResolveProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sky1stCharacterStudio.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static string FindDefaultGameRoot()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Sora No Kiseki the 1st"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "Sora No Kiseki the 1st")
        };
        return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "pac", "steam", "asset_common_model.pac"))) ?? "";
    }
}
