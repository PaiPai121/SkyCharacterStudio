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
    private bool _applyingLanguage;
    private string? _statusKey = "status.ready";
    private object?[] _statusArgs = Array.Empty<object?>();
    private bool _statusError;

    public MainWindow()
    {
        InitializeComponent();
        UiText.Initialize();
        _projectRoot = ResolveProjectRoot();
        _previewService = new PreviewService(_projectRoot);
        GamePathBox.Text = FindDefaultGameRoot();
        var savedPath = Path.Combine(_projectRoot, "game-directory.txt");
        if (File.Exists(savedPath)) GamePathBox.Text = File.ReadAllText(savedPath).Trim();
        CharacterBox.ItemsSource = _visibleCharacters;
        ContourPreview.Strength = ShapeSlider.Value;
        _applyingLanguage = true;
        LanguageBox.SelectedIndex = UiText.IsEnglish ? 1 : 0;
        ApplyLanguage();
        _applyingLanguage = false;
        Loaded += MainWindow_Loaded;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        var language = string.Equals(item.Tag?.ToString(), "en", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.English : UiLanguage.Chinese;
        UiText.SetLanguage(language);
        ApplyLanguage();
        SetStatusKey("status.language.changed");
    }

    private void ApplyLanguage()
    {
        Title = UiText.T("window.title");
        AppHeadingText.Text = UiText.T("app.heading");
        AppSubtitleText.Text = UiText.T("app.subtitle");
        OfflineText.Text = UiText.T("offline");
        ScanHeadingText.Text = UiText.T("scan.panel");
        GameFolderText.Text = UiText.T("game.folder");
        BrowseButton.Content = UiText.T("browse");
        ScanButton.Content = UiText.T("scan.button");
        ModelsHeadingText.Text = UiText.T("models");
        FilterBox.ToolTip = UiText.T("filter.tooltip");
        CurrentModelLabel.Text = UiText.T("selected.model");
        LiveHeadingText.Text = UiText.T("live.heading");
        PortraitStatusText.Text = UiText.T("portrait.status");
        PreviewToggleButton.Content = _showAdjustedPreview ? UiText.T("toggle.original") : UiText.T("toggle.adjusted");
        FullBodyButton.Content = UiText.T("full.body");
        UpperBodyButton.Content = UiText.T("upper.body");
        ContourHeadingText.Text = UiText.T("contour.heading");
        ContourNoteText.Text = UiText.T("contour.note");
        AdjustmentLabelText.Text = UiText.T("adjustment.mode");
        WidthModeItem.Content = UiText.T("mode.width");
        ChestModeItem.Content = UiText.T("mode.chest");
        StrengthHelpText.Text = UiText.T("strength.help");
        ResetShapeButton.Content = UiText.T("reset");
        ResetShapeButton.ToolTip = UiText.T("reset.tooltip");
        InstallButton.Content = UiText.T("install");
        SummonTestingBox.Content = UiText.T("summon");
        RestoreButton.Content = UiText.T("restore");
        if (_statusKey is not null)
            ApplyStatusText(UiText.F(_statusKey, _statusArgs), _statusError);
        if (_allCharacters.Count == 0) ModelCountText.Text = UiText.T("not.scanned");
        if (_selectedCharacter is null)
        {
            SelectedIdText.Text = UiText.T("waiting.scan");
            SelectedMetaText.Text = "";
            ContourPreview.ModelLabel = UiText.T("contour.label");
        }
        else
        {
            ApplyFilter();
            UpdateCharacterDetails(_selectedCharacter);
        }
        if (_modelReady) RefreshLive();
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
            SetStatusKey("status.choose.folder", true);
            SetActionState(true);
            _isScanning = false;
            return;
        }

        var steamRoot = Path.Combine(root, "pac", "steam");
        var modelPath = Path.Combine(steamRoot, "asset_common_model.pac");
        var infoPath = Path.Combine(steamRoot, "asset_common_model_info.pac");
        var imagePath = Path.Combine(steamRoot, "image.pac");
        SetStatusKey("status.read.index");

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
            SetStatusKey("status.scan.complete", false, _allCharacters.Count, portraitCount);
        }
        catch (Exception exception)
        {
            _modelArchive = null;
            _imageArchive = null;
            _allCharacters.Clear();
            _visibleCharacters.Clear();
            _selectedCharacter = null;
            CharacterBox.SelectedItem = null;
            SetStatusKey("status.scan.failed", true, exception.Message);
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
                || character.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || character.LocalizedName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        _visibleCharacters.Clear();
        foreach (var record in matches) _visibleCharacters.Add(record);

        CharacterRecord? preferred = null;
        if (previousId is not null) preferred = _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals(previousId, StringComparison.OrdinalIgnoreCase));
        if (preferred is null && selectPreferred)
            preferred = _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals("chr5002", StringComparison.OrdinalIgnoreCase))
                ?? _visibleCharacters.FirstOrDefault(x => x.ModelId.Equals("chr5000", StringComparison.OrdinalIgnoreCase));
        CharacterBox.SelectedItem = preferred ?? _visibleCharacters.FirstOrDefault();
        ModelCountText.Text = _allCharacters.Count == 0 ? UiText.T("not.scanned") : $"{matches.Count}/{_allCharacters.Count}";
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
        ChestModeItem.ToolTip=UiText.T("mode.detecting");
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
        PortraitStatusText.Text=UiText.F("status.loading.model", record.LocalizedName);
        var directory=Path.Combine(_projectRoot,"cache","models",record.ModelId,mode);
        try {
            await AutoModelService.Run(GamePathBox.Text,record.ModelId,mode,directory,isBaseGameCharacter:record.IsBaseGameCharacter);
            if(generation!=_previewGeneration)return;
            LiveView.Load(directory,"model.json"); LiveView.Frame(true);
            using(var meta=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"model-meta.json")))) {
                int missing=meta.RootElement.GetProperty("missing_textures").GetArrayLength();
                _materialWarning=missing>0 ? " "+UiText.F("missing.textures", missing) : "";
                var bones=meta.RootElement.GetProperty("chest_detection");
                var recognized=bones.TryGetProperty("can_deform",out var capability) && capability.GetBoolean();
                var eligibility=record.AgeInfo.DisplayText;
                if (meta.RootElement.GetProperty("adult_eligible").GetBoolean()!=record.AdultShapeEligible)
                    throw new InvalidDataException(UiText.T("error.age.catalog"));
                var boneStatus=UiText.F("bone.detect", UiText.BoneDetail(bones.GetProperty("detail").GetString() ?? ""));
                var boneNames=bones.TryGetProperty("bones",out var boneList)
                    ? string.Join(UiText.T("bone.names.separator"),boneList.EnumerateArray().Select(b=>b.GetString())) : "";
                SelectedMetaText.Text+="\n\n"+boneStatus+(boneNames.Length>0 ? "\n"+boneNames : "")+"\n"+UiText.F("eligibility", eligibility);

                ChestModeItem.IsEnabled=record.AdultShapeEligible && recognized;
                var ageBasis=UiText.AgeBasis(record.AgeInfo.Basis, record.AgeInfo.Source);
                var ageSource=UiText.AgeSource(record.AgeInfo.Source);
                ChestModeItem.ToolTip=boneStatus+"; "+eligibility+"\n"+ageBasis+"\n"+ageSource;
                SelectedMetaText.ToolTip=ageBasis+"\n"+ageSource;
                _materialWarning+=" "+boneStatus+"; "+eligibility+".";
                if (mode=="chest" && meta.RootElement.GetProperty("deformation_gain").GetDouble()<.999)
                    _materialWarning+=" "+UiText.T("shape.limited");

            }
            _modelReady=true; LiveView.Visibility=Visibility.Visible;RefreshLive();
        } catch(Exception error) {
            if(generation==_previewGeneration) PortraitStatusText.Text=UiText.F("status.preview.failed", error.Message);
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
        LiveView.SetStrength(_showAdjustedPreview ? strength : 0, CurrentMode=="chest");
        PreviewToggleButton.Content=_showAdjustedPreview ? UiText.T("toggle.original") : UiText.T("toggle.adjusted");
        PortraitStatusText.Text=_showAdjustedPreview
            ? UiText.F("status.preview.adjusted", strength, CurrentMode=="chest" ? UiText.T("bone.chest") : UiText.T("bone.width"))
            : UiText.F("status.preview.original", strength);
        PortraitStatusText.Text+=_materialWarning;
    }

    private void UpdateCharacterDetails(CharacterRecord record)
    {
        SelectedNameText.Text = record.LocalizedName;
        SelectedIdText.Text = record.ModelId;
        SelectedMetaText.Text = UiText.F("model.size", record.ArchiveSizeText)+"\n"
            + UiText.F("model.info", record.ModelInfoEntry is null ? UiText.T("not.found") : UiText.T("found"))+"\n"
            + UiText.F("portrait", record.PreviewText)+"\n"+record.SupportText;
        ContourPreview.ModelLabel = record.ModelId.Equals("chr5002", StringComparison.OrdinalIgnoreCase)
            ? UiText.T("scherazard.contour")
            : UiText.F("model.contour", record.LocalizedName);
        PreviewToggleButton.IsEnabled = record.IsSupportedShapeEdit;
        PreviewToggleButton.Content = _showAdjustedPreview ? UiText.T("toggle.original") : UiText.T("toggle.adjusted");
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
            if (GameInstaller.IsGameRunning()) throw new InvalidOperationException(UiText.T("error.game.running"));
            var expected = Path.Combine(target,"pac","steam","asset_common_model.pac");
            if (_modelArchive is null || !Path.GetFullPath(_modelArchive.Path).Equals(expected,StringComparison.OrdinalIgnoreCase))
                await ScanAsync();
            if (_modelArchive is null || !Path.GetFullPath(_modelArchive.Path).Equals(expected,StringComparison.OrdinalIgnoreCase)
                || _selectedCharacter is null || !_modelReady)
                throw new InvalidOperationException(UiText.T("error.not.ready"));
            SetActionState(false);
            var strength=(int)Math.Round(ShapeSlider.Value);
            SetStatusKey("status.installing", false, strength, target);
            var testSummon=SummonTestingBox.IsChecked==true;
            var result=await ExportService.ExportAsync(_selectedCharacter,_modelArchive,_projectRoot,strength,true,CancellationToken.None,CurrentMode,testSummon);
            if(!result.ShapeEditApplied || result.RuntimePackagePath is null) throw new InvalidOperationException(UiText.T("error.generation"));
            var backup=await Task.Run(()=>GameInstaller.Install(result.RuntimePackagePath,target,Path.Combine(_projectRoot,"install-backups")));
            SetStatusKey(testSummon ? "status.installed.summon" : "status.installed.normal", false,
                strength, target, backup);
        } catch(Exception error) { SetStatus(error.Message,true); }
        finally { SetActionState(true); }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiText.T("choose.game.title"),
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
            SetStatusKey("status.restored", false, restored);
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

    private void SetStatusKey(string key, bool error = false, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusError = error;
        ApplyStatusText(UiText.F(key, args), error);
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusKey = null;
        _statusArgs = Array.Empty<object?>();
        _statusError = error;
        ApplyStatusText(text, error);
    }

    private void ApplyStatusText(string text, bool error)
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
