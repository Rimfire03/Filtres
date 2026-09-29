using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FiltresApp.Views.Dialogs;

/// <summary>Sélecteur de couleur par roue chromatique (Paramètres, "Couleurs de ligne") : plus simple
/// qu'un code hexadécimal à saisir à la main. La roue (teinte = angle, saturation = distance au centre,
/// toujours à luminosité maximale) est dessinée une seule fois dans un WriteableBitmap ; la luminosité
/// finale se règle séparément via le curseur, sans avoir à régénérer la roue.</summary>
public partial class ColorWheelWindow : Window
{
    private const int Size = 220;
    private readonly double _centerXy = Size / 2.0;

    private double _hue;
    private double _saturation;

    public string SelectedHex { get; private set; } = "#FFFFFF";

    public ColorWheelWindow(string initialHex)
    {
        InitializeComponent();
        WheelImage.Source = BuildWheelBitmap();

        Color initial;
        try { initial = (Color)ColorConverter.ConvertFromString(initialHex)!; }
        catch { initial = Colors.White; }
        var (h, s, v) = RgbToHsv(initial);
        _hue = h;
        _saturation = s;
        ValueSlider.Value = v;
        UpdatePreview();
    }

    private static WriteableBitmap BuildWheelBitmap()
    {
        var bmp = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[Size * Size * 4];
        var center = Size / 2.0;
        var radius = Size / 2.0;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Math.Sqrt(dx * dx + dy * dy);
                if (r > radius) continue; // laissé transparent : coins hors du cercle

                var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (angle < 0) angle += 360;
                var sat = Math.Min(1.0, r / radius);
                var (red, green, blue) = HsvToRgb(angle, sat, 1.0);

                var idx = (y * Size + x) * 4;
                pixels[idx] = blue;
                pixels[idx + 1] = green;
                pixels[idx + 2] = red;
                pixels[idx + 3] = 255;
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
        return bmp;
    }

    private void WheelImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WheelImage.CaptureMouse();
        UpdateFromPoint(e.GetPosition(WheelImage));
    }

    private void WheelImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && WheelImage.IsMouseCaptured)
            UpdateFromPoint(e.GetPosition(WheelImage));
    }

    private void WheelImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => WheelImage.ReleaseMouseCapture();

    private void UpdateFromPoint(Point p)
    {
        var dx = p.X - _centerXy;
        var dy = p.Y - _centerXy;
        var r = Math.Min(Math.Sqrt(dx * dx + dy * dy), _centerXy);
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle < 0) angle += 360;

        _hue = angle;
        _saturation = r / _centerXy;
        UpdatePreview();
    }

    private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private void UpdatePreview()
    {
        // Le Slider déclenche ValueChanged dès son initialisation en XAML (valeur par défaut Value="1"),
        // avant même que les éléments suivants (PreviewBrush, HexText) ne soient connectés par
        // InitializeComponent() - pas encore prêts à ce moment-là.
        if (PreviewBrush is null || HexText is null) return;

        var (r, g, b) = HsvToRgb(_hue, _saturation, ValueSlider.Value);
        var color = Color.FromRgb(r, g, b);
        PreviewBrush.Color = color;
        SelectedHex = $"#{r:X2}{g:X2}{b:X2}";
        HexText.Text = SelectedHex;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r1, g1, b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 0.0001)
        {
            if (max == r) hue = 60 * ((g - b) / delta % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;

        var sat = max <= 0.0001 ? 0 : delta / max;
        return (hue, sat, max);
    }
}
