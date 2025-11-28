using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoZoom;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ZoomState _zoom = new();
    private readonly VlcRenderer _vlc = new();

    private bool _isPanning = false;
    private Point _lastMouse;

    private D3DImage _mainD3DImage;
    private D3DImage _miniD3DImage;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _zoom;

        _mainD3DImage = new D3DImage();
        _miniD3DImage = new D3DImage();
        MainImage.Source = _mainD3DImage;
        MiniImage.Source = _miniD3DImage;

        PreviewMouseWheel += OnPreviewMouseWheel;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vlc.VideoW == 0 || MainImage.Source == null) return;

        var p = e.GetPosition(MainImage);
        MapMainControlToSource(p, out double srcX, out double srcY);

        double factor = e.Delta > 0 ? 1.25 : 1.0 / 1.25;
        _zoom.ZoomAtPoint(factor, srcX, srcY);

        e.Handled = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Early exit if video not loaded yet
        if (_vlc.VideoW == 0 || _vlc.VideoH == 0) return;

        // D3DRenderer is created by VLC's VideoFormat callback
        var renderer = _vlc.D3DRenderer;
        if (renderer == null) return;

        try
        {
            // Initialize zoom extents once
            if (_zoom.VideoW == 0)
            {
                _zoom.VideoW = _vlc.VideoW;
                _zoom.VideoH = _vlc.VideoH;
                _zoom.CenterX = _zoom.VideoW / 2.0;
                _zoom.CenterY = _zoom.VideoH / 2.0;

                System.Diagnostics.Debug.WriteLine($"Video initialized: {_vlc.VideoW}x{_vlc.VideoH}");
            }

            // Render the DirectX surface to our D3DImage controls
            renderer.Render(_mainD3DImage);
            renderer.Render(_miniD3DImage);

            // Compute crop rectangle for zoom
            var rect = _zoom.GetCropRect();
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Update shader parameters
            var normalizedRect = new Rect(
                (double)rect.X / _vlc.VideoW,
                (double)rect.Y / _vlc.VideoH,
                (double)rect.Width / _vlc.VideoW,
                (double)rect.Height / _vlc.VideoH
            );
            CropEffect.CropRectangle = normalizedRect;

            // Update miniature overlay rectangle
            UpdateMiniOverlayRect(rect);

            // Update seek slider
            UpdateSeekSlider();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnRendering error: {ex.Message}");
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vlc.VideoW == 0) return;

        // Only seek if user is dragging (avoid feedback loop)
        if (SeekSlider.IsMouseCaptureWithin)
        {
            double newPos = SeekSlider.Value / 100.0;
            _vlc.Position = newPos;
        }
    }

    private void UpdateMiniOverlayRect(Int32Rect cropRect)
    {
        if (_vlc.VideoW == 0 || _vlc.VideoH == 0) return;

        // Compute how MiniImage scales the source
        if (MiniImage.Source == null || MiniImage.ActualWidth == 0 || MiniImage.ActualHeight == 0) return;

        var imgW = _vlc.VideoW;
        var imgH = _vlc.VideoH;

        // Uniform scaling
        double scale = Math.Min(MiniImage.ActualWidth / imgW, MiniImage.ActualHeight / imgH);
        double drawW = imgW * scale;
        double drawH = imgH * scale;

        // Image draws centered inside its layout slot
        double offsetX = (MiniImage.ActualWidth - drawW) / 2.0;
        double offsetY = (MiniImage.ActualHeight - drawH) / 2.0;

        // Map cropRect from source pixels to mini pixels
        double x = offsetX + cropRect.X * scale;
        double y = offsetY + cropRect.Y * scale;
        double w = cropRect.Width * scale;
        double h = cropRect.Height * scale;

        Canvas.SetLeft(MiniViewRect, x);
        Canvas.SetTop(MiniViewRect, y);
        MiniViewRect.Width = w;
        MiniViewRect.Height = h;

        // Stretch overlay canvas to same size as MiniImage layout
        MiniOverlay.Width = MiniImage.ActualWidth;
        MiniOverlay.Height = MiniImage.ActualHeight;
    }

    private void UpdateSeekSlider()
    {
        if (_vlc.VideoW == 0) return;

        try
        {
            var pos = _vlc.Position;
            if (!SeekSlider.IsMouseCaptureWithin)
                SeekSlider.Value = pos * 100; // VLC position is 0.0–1.0
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateSeekSlider error: {ex.Message}");
        }
    }

    // -------------------- UI commands ---------------------

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _vlc.Open(dlg.FileName);
                _zoom.CurrentOpenedFile = dlg.FileName;
                _vlc.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _vlc.Play();
    private void Pause_Click(object sender, RoutedEventArgs e) => _vlc.Pause();
    private void Stop_Click(object sender, RoutedEventArgs e) => _vlc.Stop();

    // -------------------- Mouse interactions ---------------------

    private void MainHitLayer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vlc.VideoW == 0) return;

        var pos = e.GetPosition(MainImage);
        if (MainImage.Source == null) return;

        double srcX, srcY;
        MapMainControlToSource(pos, out srcX, out srcY);

        double factor = e.Delta > 0 ? 1.25 : (1.0 / 1.25);
        _zoom.ZoomAtPoint(factor, srcX, srcY);
    }

    private void MainHitLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isPanning = true;
            _lastMouse = e.GetPosition(MainHitLayer);
            MainHitLayer.CaptureMouse();
            MainHitLayer.Cursor = Cursors.SizeAll;
        }
    }

    private void MainHitLayer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var cur = e.GetPosition(MainHitLayer);
        var delta = cur - _lastMouse;
        _lastMouse = cur;

        // Convert delta from control pixels to source pixels
        var scale = GetMainScaleToSource();
        double dxSrc = -delta.X * scale.scaleX;
        double dySrc = -delta.Y * scale.scaleY;
        _zoom.PanBy(dxSrc, dySrc);
    }

    private void MainHitLayer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isPanning = false;
            MainHitLayer.ReleaseMouseCapture();

            if (MainHitLayer.IsMouseOver)
                MainHitLayer.Cursor = Cursors.Hand;
            else
                MainHitLayer.ClearValue(CursorProperty);
        }
    }

    // Map a point in MainImage layout space -> source pixel space
    private void MapMainControlToSource(Point p, out double srcX, out double srcY)
    {
        var rect = _zoom.GetCropRect();
        srcX = rect.X + p.X * rect.Width / Math.Max(1, MainImage.ActualWidth);
        srcY = rect.Y + p.Y * rect.Height / Math.Max(1, MainImage.ActualHeight);
    }

    // For pan speed: how many source pixels per 1 control pixel
    private (double scaleX, double scaleY) GetMainScaleToSource()
    {
        var rect = _zoom.GetCropRect();
        double sx = rect.Width / Math.Max(1, MainImage.ActualWidth);
        double sy = rect.Height / Math.Max(1, MainImage.ActualHeight);
        return (sx, sy);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        CompositionTarget.Rendering -= OnRendering;
        _vlc.Dispose();
    }
}