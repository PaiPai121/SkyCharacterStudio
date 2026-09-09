using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Sky1stCharacterStudio;

public sealed class ShapePreviewControl : FrameworkElement
{
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(ShapePreviewControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModelLabelProperty = DependencyProperty.Register(
        nameof(ModelLabel), typeof(string), typeof(ShapePreviewControl),
        new FrameworkPropertyMetadata("模型轮廓", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public string ModelLabel
    {
        get => (string)GetValue(ModelLabelProperty);
        set => SetValue(ModelLabelProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 27, 40)), null, new Rect(0, 0, width, height));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(36, 114, 160, 170)), 1);
        for (var x = 20d; x < width; x += 32) drawing.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
        for (var y = 18d; y < height; y += 32) drawing.DrawLine(gridPen, new Point(0, y), new Point(width, y));

        var factor = Math.Clamp(Strength / 100d, 0d, 1d);
        var centerX = width * 0.5;
        var floor = height - 30;
        var headRadius = Math.Min(34, height * 0.14);
        var headCenter = new Point(centerX, 92);
        var torsoWidth = 80 + 32 * factor;
        var chestWidth = 50 + 35 * factor;
        var chestDepth = 8 + 10 * factor;
        var shoulderY = headCenter.Y + headRadius + 23;
        var waistY = Math.Min(height - 124, shoulderY + 112);
        var hipWidth = 84 + 10 * factor;

        var outline = new Pen(new SolidColorBrush(Color.FromRgb(215, 177, 91)), 2.2);
        var fill = new SolidColorBrush(Color.FromArgb(210, 48, 73, 90));
        var highlight = new SolidColorBrush(Color.FromArgb(190, 77, 190, 177));
        var soft = new SolidColorBrush(Color.FromArgb(95, 222, 185, 106));

        drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(196, 151, 105)), outline, headCenter, headRadius, headRadius * 1.06);
        drawing.DrawRoundedRectangle(fill, outline, new Rect(centerX - 15, shoulderY - 14, 30, 28), 10, 10);

        var torso = new StreamGeometry();
        using (var context = torso.Open())
        {
            context.BeginFigure(new Point(centerX - torsoWidth / 2, shoulderY), true, true);
            context.LineTo(new Point(centerX + torsoWidth / 2, shoulderY), true, false);
            context.LineTo(new Point(centerX + hipWidth / 2, waistY), true, false);
            context.LineTo(new Point(centerX - hipWidth / 2, waistY), true, false);
        }
        torso.Freeze();
        drawing.DrawGeometry(fill, outline, torso);

        drawing.DrawEllipse(highlight, outline, new Point(centerX - chestWidth * 0.48, shoulderY + 30), chestWidth * 0.28 + chestDepth, 26 + chestDepth);
        drawing.DrawEllipse(highlight, outline, new Point(centerX + chestWidth * 0.48, shoulderY + 30), chestWidth * 0.28 + chestDepth, 26 + chestDepth);
        drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(170, 215, 177, 91)), 1.4),
            new Point(centerX, shoulderY + 8), new Point(centerX, waistY - 8));

        var legGap = 11d;
        var legTop = waistY;
        var legBottom = floor;
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180, 47, 63, 79)), outline,
            new Rect(centerX - hipWidth / 2 + 10, legTop, hipWidth / 2 - legGap, legBottom - legTop), 12, 12);
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180, 47, 63, 79)), outline,
            new Rect(centerX + legGap, legTop, hipWidth / 2 - 10, legBottom - legTop), 12, 12);

        var caption = new FormattedText(
            $"{ModelLabel}  ·  形体强度 {Strength:0}%",
            CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei"), 13, new SolidColorBrush(Color.FromRgb(214, 229, 233)), 1.0);
        drawing.DrawText(caption, new Point(16, 12));
        var note = new FormattedText("胸廓与相邻衣料的局部轮廓示意", CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 11,
            new SolidColorBrush(Color.FromRgb(143, 170, 179)), 1.0);
        drawing.DrawText(note, new Point(16, height - note.Height - 9));
        drawing.DrawLine(new Pen(soft, 2), new Point(16, height - 12), new Point(92 + 1.8 * Strength, height - 12));
    }
}
