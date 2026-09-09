from pathlib import Path
p=Path(__file__).resolve().parents[1]/'MainWindow.xaml.cs'
s=p.read_text(encoding='utf-8-sig')
a=s.index('    private async void CharacterBox_SelectionChanged')
b=s.index('    private void UpdateCharacterDetails',a)
s=s[:a]+'''    private async void CharacterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ++_previewGeneration;
        LiveView.Visibility = Visibility.Collapsed;
        if (CharacterBox.SelectedItem is not CharacterRecord record) { _selectedCharacter=null; return; }
        _selectedCharacter=record;
        _showAdjustedPreview=true;
        UpdateCharacterDetails(record);
        if(record.IsSupportedShapeEdit)
        {
            try {
                if(LiveView.SourceMeshes.Count==0) LiveView.Load(Path.Combine(_projectRoot,"assets","live"));
                LiveView.Visibility=Visibility.Visible;
                RefreshLive();
            } catch(Exception error) { PortraitStatusText.Text="模型加载失败："+error.Message; }
            return;
        }
        PortraitImage.Source=PreviewService.CreatePlaceholder(record.ModelId,record.DisplayName);
        var generation=_previewGeneration;
        var preview=await _previewService.LoadAsync(record,_imageArchive,CancellationToken.None);
        if(generation!=_previewGeneration) return;
        PortraitImage.Source=preview;
        PortraitStatusText.Text="该角色尚未接入形体编辑，仅显示参考图。";
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
        if(LiveView is null || ShapeSlider is null || _selectedCharacter?.IsSupportedShapeEdit!=true) return;
        var strength=(int)Math.Round(ShapeSlider.Value);
        LiveView.SetStrength(_showAdjustedPreview ? strength : 0);
        PreviewToggleButton.Content=_showAdjustedPreview ? "切换原版" : "返回当前调整";
        PortraitStatusText.Text=_showAdjustedPreview
            ? $"实时预览：{strength}% · 导出使用同一强度。原版100%的最大局部位移约14毫米，变化较细微。"
            : $"正在对照原版（0%）；返回当前调整可查看 {strength}%。";
    }

'''+s[b:]
s=s.replace('ContourPreview.Strength = strength;','ContourPreview.Strength = strength;\n        _showAdjustedPreview=true;\n        RefreshLive();')
s=s.replace('ShapeSlider.IsEnabled = enabled && _selectedCharacter is not null;', 'ShapeSlider.IsEnabled = enabled && _selectedCharacter?.IsSupportedShapeEdit == true;')
p.write_text(s,encoding='utf-8')
