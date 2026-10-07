using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Sky1stCharacterStudio;

public partial class MainWindow : Window
{
    private readonly string _projectRoot;
    private readonly ObservableCollection<CharacterRecord> _visibleCharacters = new();
    private List<CharacterRecord> _allCharacters = new();
    private PacArchive? _modelArchive;
    private PacArchive? _imageArchive;
    private CharacterRecord? _selectedCharacter;
    private int _previewGeneration;
    private CancellationTokenSource? _previewCancellation;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _previewClock = new();
    private bool _isScanning;
    private GameEdition? _edition;
    private string? _scannedGameRoot;
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
        _previewTimer.Tick += (_, _) => PreviewElapsedText.Text = UiText.F("preview.elapsed", (int)_previewClock.Elapsed.TotalSeconds);
        GamePathBox.Text = FindDefaultGameRoot();
        var savedPath = Path.Combine(_projectRoot, "game-directory.txt");
        if (File.Exists(savedPath))
        {
            var savedGame = File.ReadAllText(savedPath).Trim();
            try { GamePathBox.Text = GameInstaller.ValidateGameRoot(savedGame); }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException) { }
        }
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
        UpdateTargetGameDisplay();
        BrowseButton.Content = UiText.T("browse");
        ScanButton.Content = UiText.T("scan.button");
        ModelsHeadingText.Text = UiText.T("models");
        FilterBox.ToolTip = UiText.T("filter.tooltip");
        CurrentModelLabel.Text = UiText.T("selected.model");
        LiveHeadingText.Text = UiText.T("live.heading");
        if (_previewClock.IsRunning) {
            PreviewLoadingHintText.Text = UiText.T("preview.loading.hint");
            PreviewElapsedText.Text = UiText.F("preview.elapsed", (int)_previewClock.Elapsed.TotalSeconds);
        } else PreviewLoadingHintText.Text = UiText.T("preview.select.hint");
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
        InstallButton.ToolTip = UiText.T("install.tooltip");
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

    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTargetGameDisplay();
        if (_scannedGameRoot is null) return;
        var selected = GamePathBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(selected, _scannedGameRoot, StringComparison.OrdinalIgnoreCase)) return;

        _scannedGameRoot = null;
        ++_previewGeneration;
        CancelPreview();
        _modelReady = false;
        _modelArchive = null;
        _imageArchive = null;
        _selectedCharacter = null;
        _allCharacters.Clear();
        _visibleCharacters.Clear();
        CharacterBox.SelectedItem = null;
        ModelCountText.Text = UiText.T("not.scanned");
        SelectedNameText.Text = "—";
        SelectedIdText.Text = UiText.T("waiting.scan");
        SelectedMetaText.Text = "";
        PortraitStatusText.Text = UiText.T("portrait.status");
        LiveView.Visibility = Visibility.Collapsed;
        ShowPreviewWaiting("preview.waiting");
        SetStatusKey("status.game.changed");
        SetActionState(true);
    }

    private void UpdateTargetGameDisplay()
    {
        if (TargetGameText is null || GamePathBox is null) return;
        var selected = GamePathBox.Text.Trim();
        _edition = null;
        var key = "game.target.none";
        if (!string.IsNullOrWhiteSpace(selected))
        {
            key = "game.target.unknown";
            try
            {
                _edition = GameEditionInfo.Detect(Path.GetFullPath(selected));
                key = _edition == GameEdition.Second ? "game.target.second" : "game.target.first";
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException) { }
        }
        TargetGameText.Text = UiText.T(key);
        TargetGameText.ToolTip = selected;
        if (_edition == GameEdition.Second && SummonTestingBox is not null)
            SummonTestingBox.IsChecked = false;
    }

    private async Task ScanAsync()
    {
        if (_isScanning) return;
        _isScanning = true;
        _scannedGameRoot = null;
        ++_previewGeneration;
        CancelPreview();
        _modelReady = false;
        _modelArchive = null;
        _imageArchive = null;
        _selectedCharacter = null;
        _allCharacters.Clear();
        _visibleCharacters.Clear();
        CharacterBox.SelectedItem = null;
        ModelCountText.Text = UiText.T("not.scanned");
        SelectedNameText.Text = "—";
        SelectedIdText.Text = UiText.T("waiting.scan");
        SelectedMetaText.Text = "";
        LiveView.Visibility = Visibility.Collapsed;
        ShowPreviewWaiting("status.read.index");
        SetActionState(false);
        var root = GamePathBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root))
        {
            SetStatusKey("status.choose.folder", true);
            SetActionState(true);
            _isScanning = false;
            return;
        }

        SetStatusKey("status.read.index");

        try
        {
            root = GameInstaller.ValidateGameRoot(root);
            var edition = GameEditionInfo.Detect(root);
            var scanRoot = root;
            var result = await Task.Run(() =>
            {
                var model = PacArchive.LoadGameResources(scanRoot, "asset_common_model.pac", true,
                    "asset/common/model")!;
                var modelInfo = PacArchive.LoadGameResources(scanRoot, "asset_common_model_info.pac", false,
                    "asset/common/model_info");
                var image = PacArchive.LoadGameResources(scanRoot, "image.pac", false,
                    "asset/dx11/image", "asset/common/image");
                var characters = CharacterScanner.Build(model, modelInfo, image);
                if (characters.Count == 0) throw new InvalidDataException(UiText.T("error.model.none"));
                return (model, image, characters);
            });
            _modelArchive = result.model;
            _imageArchive = result.image;
            _edition = edition;
            if (edition == GameEdition.Second) SummonTestingBox.IsChecked = false;
            InstallButton.Content = UiText.T("install");
            InstallButton.ToolTip = UiText.T("install.tooltip");
            _allCharacters = result.characters;
            _scannedGameRoot = root;
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
            _scannedGameRoot = null;
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
        ++_previewGeneration; CancelPreview(); _modelReady=false; LiveView.Visibility=Visibility.Collapsed;
        if(CharacterBox.SelectedItem is not CharacterRecord record) {_selectedCharacter=null; ShowPreviewWaiting("preview.waiting"); return;}
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
        CancelPreview();
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _modelReady=false; LiveView.Visibility=Visibility.Collapsed;
        BeginPreviewLoading(record.LocalizedName);
        SetActionState(false); UpdateCharacterDetails(record);
        PortraitStatusText.Text=UiText.F("status.loading.model", record.LocalizedName);
        var directory=Path.Combine(_projectRoot,"cache","models",record.Edition.ToString(),record.ModelId,mode);
        try {
            await AutoModelService.Run(GamePathBox.Text,record.ModelId,mode,directory,
                isBaseGameCharacter:record.IsBaseGameCharacter,cancellationToken:cancellation.Token,
                modelSource:record.ModelEntry.LoosePath ?? _modelArchive?.ArchivePath,
                imageArchive:_imageArchive?.ArchivePath,ageDefinitionLabel:record.AgeDefinitionLabel);
            if(generation!=_previewGeneration)return;
            PreviewStageText.Text = UiText.T("preview.stage.render");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
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
                if (record.Edition == GameEdition.Second) {
                    var borrowed = meta.RootElement.GetProperty("borrowed_model_ids").EnumerateArray()
                        .Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                    if (borrowed.Length > 0)
                        SelectedMetaText.Text += "\n" + UiText.F("second.borrowed", string.Join(", ", borrowed));
                    var aligned = meta.RootElement.GetProperty("preview_alignment").GetArrayLength();
                    if (aligned > 0)
                        SelectedMetaText.Text += "\n" + UiText.F("second.aligned", aligned);
                }

                ChestModeItem.IsEnabled=record.AdultShapeEligible && recognized;
                var ageBasis=UiText.AgeBasis(record.AgeInfo.Basis, record.AgeInfo.Source);
                var ageSource=UiText.AgeSource(record.AgeInfo.Source);
                ChestModeItem.ToolTip=boneStatus+"; "+eligibility+"\n"+ageBasis+"\n"+ageSource;
                SelectedMetaText.ToolTip=ageBasis+"\n"+ageSource;
                _materialWarning+=" "+boneStatus+"; "+eligibility+".";
                if (mode=="chest" && meta.RootElement.GetProperty("deformation_gain").GetDouble()<.999)
                    _materialWarning+=" "+UiText.T("shape.limited");

            }
            _modelReady=true; LiveView.Visibility=Visibility.Visible;PreviewLoadingPanel.Visibility=Visibility.Collapsed;RefreshLive();
        } catch(OperationCanceledException) when (cancellation.IsCancellationRequested) {
            // A newer selection owns the preview pane.
        } catch(Exception error) {
            if(generation==_previewGeneration) {
                ShowPreviewWaiting("preview.failed", error.Message);
                PortraitStatusText.Text=UiText.F("status.preview.failed", error.Message);
            }
        } finally {
            if(generation==_previewGeneration) {
                EndPreviewLoading();
                _previewCancellation=null;
                SetActionState(true);
            }
        }
    }

    private void CancelPreview() => _previewCancellation?.Cancel();

    private void BeginPreviewLoading(string name)
    {
        _previewClock.Restart();
        _previewTimer.Start();
        PreviewLoadingPanel.Visibility=Visibility.Visible;
        PreviewProgressBar.Visibility=Visibility.Visible;
        PreviewStageText.Text=UiText.F("status.loading.model",name);
        PreviewLoadingHintText.Text=UiText.T("preview.loading.hint");
        PreviewElapsedText.Text=UiText.F("preview.elapsed",0);
    }

    private void EndPreviewLoading()
    {
        _previewTimer.Stop();
        _previewClock.Stop();
        PreviewProgressBar.Visibility=Visibility.Collapsed;
    }

    private void ShowPreviewWaiting(string key, params object[] args)
    {
        EndPreviewLoading();
        PreviewLoadingPanel.Visibility=Visibility.Visible;
        PreviewStageText.Text=UiText.F(key,args);
        PreviewLoadingHintText.Text=UiText.T("preview.select.hint");
        PreviewElapsedText.Text="";
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
            string? installBlocker = null;
            try { GameInstaller.ValidateInstallTarget(target); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or FileNotFoundException)
            { installBlocker = error.Message; }
            if (GameInstaller.IsGameRunning(target)) installBlocker = UiText.T("error.game.running");
            string? secondLoader = null;
            if (installBlocker is null && GameEditionInfo.Detect(target) == GameEdition.Second)
            {
                try { secondLoader = SecondLoaderService.FindAvailable(target, _projectRoot); }
                catch (FileNotFoundException error) { installBlocker = error.Message; }
                if (secondLoader is null && installBlocker is null)
                    installBlocker = UiText.T("error.second.loader.missing");
            }
            var testSummon=SummonTestingBox.IsChecked==true;
            if (_modelArchive is null || !string.Equals(_scannedGameRoot, target, StringComparison.OrdinalIgnoreCase))
                await ScanAsync();
            if (_modelArchive is null || !string.Equals(_scannedGameRoot, target, StringComparison.OrdinalIgnoreCase)
                || _selectedCharacter is null || !_modelReady)
                throw new InvalidOperationException(UiText.T("error.not.ready"));
            SetActionState(false);
            var strength=(int)Math.Round(ShapeSlider.Value);
            if (installBlocker is null) SetStatusKey("status.installing", false, strength, target);
            else SetStatusKey("status.exporting.only", false, strength, installBlocker);
            var result=await ExportService.ExportAsync(_selectedCharacter,_modelArchive,_projectRoot,strength,
                installBlocker is null,CancellationToken.None,CurrentMode,testSummon && installBlocker is null,secondLoader);
            if(!result.ShapeEditApplied || !File.Exists(result.ModelPath)) throw new InvalidOperationException(UiText.T("error.generation"));
            if (installBlocker is not null) {
                SetStatusKey("status.exported.only", false, result.ModelPath, installBlocker);
                return;
            }
            if (result.RuntimePackagePath is null) throw new InvalidOperationException(UiText.T("error.generation"));
            var backup=await Task.Run(()=>GameInstaller.Install(result.RuntimePackagePath,target,Path.Combine(_projectRoot,"install-backups")));
            if (_selectedCharacter.Edition == GameEdition.Second)
                SetStatusKey("status.installed.second", false, strength, target, backup);
            else if (result.SummonEnabled) SetStatusKey("status.installed.summon", false, strength, target, backup);
            else if (result.SummonWarning is not null)
                SetStatusKey("status.installed.summon.skipped", false, strength, target, backup, result.SummonWarning);
            else SetStatusKey("status.installed.normal", false, strength, target, backup);
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
        if(SummonTestingBox is not null)SummonTestingBox.IsEnabled=enabled && _edition == GameEdition.First;
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
            if (File.Exists(Path.Combine(directory.FullName, "SkyCharacterStudio.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static string FindDefaultGameRoot()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string registered
                && Path.IsPathFullyQualified(registered)) steamRoots.Add(registered);
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Security.SecurityException) { }
        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steam in steamRoots)
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    var library = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (Path.IsPathFullyQualified(library)) libraries.Add(library);
                }
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        foreach (var library in libraries)
        foreach (var name in new[] { "Sora No Kiseki the 1st", "Trails in the Sky 2nd Chapter" })
        {
            var candidate = Path.Combine(library, "steamapps", "common", name);
            if (File.Exists(Path.Combine(candidate, "sora_1st.exe")) != File.Exists(Path.Combine(candidate, "sora_2nd.exe")))
                return candidate;
        }
        return "";
    }
}
