using System;
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

    private bool _noGpu = false;
    private bool _isPanning = false;
    private bool _isMiniDragging = false;
    private Point _lastMouse;
    private double _miniDragOffsetX;
    private double _miniDragOffsetY;

    private D3DImage _mainD3DImage;
    private D3DImage _miniD3DImage;

    private WriteableBitmap _fallbackBitmap;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _zoom;

        _mainD3DImage = new D3DImage();
        _miniD3DImage = new D3DImage();
        _mainD3DImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
        _miniD3DImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;

        MainImage.Source = _mainD3DImage;
        MiniImage.Source = _miniD3DImage;

        PreviewMouseWheel += OnPreviewMouseWheel;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnFrontBufferAvailableChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        var img = sender as D3DImage; if (img == null) return;
        // Respect No GPU flag: always use CPU fallback
        if (_noGpu)
        {
            EnsureFallbackBitmap();
            if (MainImage.Source != _fallbackBitmap) MainImage.Source = _fallbackBitmap;
            if (MiniImage.Source != _fallbackBitmap) MiniImage.Source = _fallbackBitmap;
            return;
        }

        if (img.IsFrontBufferAvailable && _vlc.D3DRenderer != null)
        {
            try { _vlc.D3DRenderer.Render(img); } catch { }
            if (MainImage.Source != _mainD3DImage) MainImage.Source = _mainD3DImage;
            if (MiniImage.Source != _miniD3DImage) MiniImage.Source = _miniD3DImage;
        }
        else
        {
            EnsureFallbackBitmap();
            if (MainImage.Source != _fallbackBitmap) MainImage.Source = _fallbackBitmap;
            if (MiniImage.Source != _fallbackBitmap) MiniImage.Source = _fallbackBitmap;
        }
    }

    private void MiniOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _vlc.VideoW <= 0 || _vlc.VideoH <= 0)
            return;

        var mouse = e.GetPosition(MiniOverlay);
        if (!TryMapMiniControlToSource(mouse, out double srcX, out double srcY))
            return;

        var crop = _zoom.GetCropRect();
        bool isInsideViewRect = srcX >= crop.X && srcX <= crop.X + crop.Width &&
                                srcY >= crop.Y && srcY <= crop.Y + crop.Height;
        if (!isInsideViewRect)
            return;

        _isMiniDragging = true;
        _miniDragOffsetX = srcX - crop.X;
        _miniDragOffsetY = srcY - crop.Y;
        MiniOverlay.CaptureMouse();
        MiniOverlay.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void MiniOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMiniDragging || _vlc.VideoW <= 0 || _vlc.VideoH <= 0)
            return;

        var mouse = e.GetPosition(MiniOverlay);
        if (!TryMapMiniControlToSource(mouse, out double srcX, out double srcY))
            return;

        var crop = _zoom.GetCropRect();
        double cropW = crop.Width;
        double cropH = crop.Height;

        double newCropX = srcX - _miniDragOffsetX;
        double newCropY = srcY - _miniDragOffsetY;

        double minCenterX = cropW / 2.0;
        double maxCenterX = _vlc.VideoW - cropW / 2.0;
        double minCenterY = cropH / 2.0;
        double maxCenterY = _vlc.VideoH - cropH / 2.0;

        _zoom.CenterX = Math.Clamp(newCropX + cropW / 2.0, minCenterX, maxCenterX);
        _zoom.CenterY = Math.Clamp(newCropY + cropH / 2.0, minCenterY, maxCenterY);
        e.Handled = true;
    }

    private void MiniOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _isMiniDragging = false;
        MiniOverlay.ReleaseMouseCapture();
        MiniOverlay.ClearValue(CursorProperty);
        e.Handled = true;
    }

    private void EnsureFallbackBitmap()
    {
        if (_vlc.VideoW > 0 && _vlc.VideoH > 0)
        {
            if (_fallbackBitmap == null ||
                _fallbackBitmap.PixelWidth != _vlc.VideoW ||
                _fallbackBitmap.PixelHeight != _vlc.VideoH)
            {
                _fallbackBitmap = new WriteableBitmap(_vlc.VideoW, _vlc.VideoH, 96, 96, PixelFormats.Bgra32, null);
            }
        }
    }

    unsafe void CopyToFallback()
    {
        EnsureFallbackBitmap();
        if (_fallbackBitmap == null) return;

        _fallbackBitmap.Lock();
        try
        {
            IntPtr dst = _fallbackBitmap.BackBuffer;
            int dstStride = _fallbackBitmap.BackBufferStride;

            int maxRows = _fallbackBitmap.PixelHeight;
            int maxBytesPerRow = Math.Min(Math.Abs(dstStride), _fallbackBitmap.PixelWidth * 4);

            if (!_vlc.CopyLatestBgraFrameTo(
                    dst,
                    dstStride,
                    maxRows,
                    maxBytesPerRow,
                    out int copiedRows,
                    out int copiedBytesPerRow))
            {
                return;
            }

            if (copiedRows <= 0 || copiedBytesPerRow <= 0)
                return;

            int dirtyWidth = Math.Min(_fallbackBitmap.PixelWidth, copiedBytesPerRow / 4);
            _fallbackBitmap.AddDirtyRect(new Int32Rect(0, 0, dirtyWidth, copiedRows));
        }
        finally
        {
            _fallbackBitmap.Unlock();
        }
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
        if (_vlc.VideoW == 0 || _vlc.VideoH == 0) 
            return; 
        
        var renderer = _vlc.D3DRenderer; 
        
        if (renderer == null && !_noGpu) 
            return;
        
        try
        {
            if (_zoom.VideoW == 0)
            {
                _zoom.VideoW = _vlc.VideoW;
                _zoom.VideoH = _vlc.VideoH;
                _zoom.CenterX = _zoom.VideoW / 2.0;
                _zoom.CenterY = _zoom.VideoH / 2.0;
            }

            // Force CPU path when No GPU is enabled
            if (!_noGpu && _mainD3DImage.IsFrontBufferAvailable)
            {
                renderer.Render(_mainD3DImage);
                renderer.Render(_miniD3DImage);
            }
            else
            {
                CopyToFallback();

                // Ensure both images display the CPU bitmap
                if (MainImage.Source != _fallbackBitmap) MainImage.Source = _fallbackBitmap;
                if (MiniImage.Source != _fallbackBitmap) MiniImage.Source = _fallbackBitmap;
            }

            var rect = _zoom.GetCropRect();
            if (rect.Width <= 0 || rect.Height <= 0) return;

            var normalizedRect = new Rect(
                (double)rect.X / _vlc.VideoW,
                (double)rect.Y / _vlc.VideoH,
                (double)rect.Width / _vlc.VideoW,
                (double)rect.Height / _vlc.VideoH);
            CropEffect.CropRectangle = normalizedRect;

            UpdateMiniOverlayRect(rect);
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

    private void NoGpu_Checked(object sender, RoutedEventArgs e)
    {
        _noGpu = true;
        // Force CPU sources for both images
        EnsureFallbackBitmap();
        MainImage.Source = _fallbackBitmap;
        MiniImage.Source = _fallbackBitmap;
    }
    private void NoGpu_Unchecked(object sender, RoutedEventArgs e)
    {
        _noGpu = false;
        // Switch back to D3D if available
        if (_vlc.D3DRenderer != null)
        {
            try
            {
                if (_mainD3DImage.IsFrontBufferAvailable)
                {
                    _vlc.D3DRenderer.Render(_mainD3DImage);
                    MainImage.Source = _mainD3DImage;
                }
                if (_miniD3DImage.IsFrontBufferAvailable)
                {
                    _vlc.D3DRenderer.Render(_miniD3DImage);
                    MiniImage.Source = _miniD3DImage;
                }
            }
            catch { /* keep UI responsive even if renderer throws */ }
        }
    }

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

    private bool TryMapMiniControlToSource(Point p, out double srcX, out double srcY)
    {
        srcX = 0;
        srcY = 0;

        if (_vlc.VideoW <= 0 || _vlc.VideoH <= 0 || MiniImage.ActualWidth <= 0 || MiniImage.ActualHeight <= 0)
            return false;

        double scale = Math.Min(MiniImage.ActualWidth / _vlc.VideoW, MiniImage.ActualHeight / _vlc.VideoH);
        if (scale <= 0)
            return false;

        double drawW = _vlc.VideoW * scale;
        double drawH = _vlc.VideoH * scale;
        double offsetX = (MiniImage.ActualWidth - drawW) / 2.0;
        double offsetY = (MiniImage.ActualHeight - drawH) / 2.0;

        double px = Math.Clamp(p.X, offsetX, offsetX + drawW);
        double py = Math.Clamp(p.Y, offsetY, offsetY + drawH);

        srcX = (px - offsetX) / scale;
        srcY = (py - offsetY) / scale;
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        CompositionTarget.Rendering -= OnRendering;
        _vlc.Dispose();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        _vlc.InitializeWithHwnd(hwnd); // Supply HWND before video format negotiation
    }
}