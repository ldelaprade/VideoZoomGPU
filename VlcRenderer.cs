using System;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace VideoZoom;

public sealed class VlcRenderer : IDisposable
{
    private readonly object _frameSync = new();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;

    private IntPtr _latestFrame = IntPtr.Zero;
    private int _frameBytes;
    private IntPtr _hostHwnd = IntPtr.Zero;

    public D3D9Renderer? D3DRenderer { get; private set; }

    public int VideoW { get; private set; }
    public int VideoH { get; private set; }

    public double Position
    {
        get => _mediaPlayer.Position;
        set
        {
            var clamped = Math.Clamp(value, 0d, 1d);
            _mediaPlayer.Position = (float)clamped;
        }
    }

    public VlcRenderer()
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);

        _mediaPlayer.SetVideoFormatCallbacks(VideoFormat, new MediaPlayer.LibVLCVideoCleanupCb(VideoCleanup));
        _mediaPlayer.SetVideoCallbacks(LockVideo, UnlockVideo, null);
    }

    public void InitializeWithHwnd(IntPtr hwnd)
    {
        _hostHwnd = hwnd;

        lock (_frameSync)
        {
            if (_hostHwnd != IntPtr.Zero && VideoW > 0 && VideoH > 0)
            {
                RecreateD3DRenderer();
            }
        }
    }

    public void Open(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A valid file path is required.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);

        _media?.Dispose();
        _media = new Media(_libVlc, new Uri(filePath));
        _mediaPlayer.Media = _media;
    }

    public void Play() => _mediaPlayer.Play();
    public void Pause() => _mediaPlayer.Pause();
    public void Stop() => _mediaPlayer.Stop();

    public unsafe bool CopyLatestBgraFrameTo(
        IntPtr destination,
        int destinationStride,
        int maxRows,
        int maxBytesPerRow,
        out int rowsCopied,
        out int bytesPerRowCopied)
    {
        rowsCopied = 0;
        bytesPerRowCopied = 0;

        if (destination == IntPtr.Zero || destinationStride <= 0 || maxRows <= 0 || maxBytesPerRow <= 0)
            return false;

        lock (_frameSync)
        {
            if (_latestFrame == IntPtr.Zero || VideoW <= 0 || VideoH <= 0)
                return false;

            int srcStride = VideoW * 4;
            int rows = Math.Min(VideoH, maxRows);
            int bytesPerRow = Math.Min(srcStride, Math.Min(destinationStride, maxBytesPerRow));

            for (int y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(
                    (void*)(_latestFrame + y * srcStride),
                    (void*)(destination + y * destinationStride),
                    destinationStride,
                    bytesPerRow);
            }

            rowsCopied = rows;
            bytesPerRowCopied = bytesPerRow;
            return true;
        }
    }

    private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        lock (_frameSync)
        {
            unsafe
            {
                var fourCc = (byte*)chroma;
                fourCc[0] = (byte)'R';
                fourCc[1] = (byte)'V';
                fourCc[2] = (byte)'3';
                fourCc[3] = (byte)'2';
            }

            VideoW = (int)width;
            VideoH = (int)height;

            pitches = width * 4;
            lines = height;

            int requiredBytes = checked((int)(pitches * lines));
            EnsureFrameBuffer(requiredBytes);

            if (_hostHwnd != IntPtr.Zero && VideoW > 0 && VideoH > 0)
            {
                RecreateD3DRenderer();
            }

            return 1;
        }
    }

    private void VideoCleanup(ref IntPtr opaque)
    {
        lock (_frameSync)
        {
            VideoW = 0;
            VideoH = 0;
        }
    }

    private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
    {
        lock (_frameSync)
        {
            if (_latestFrame == IntPtr.Zero)
                return IntPtr.Zero;

            unsafe
            {
                ((IntPtr*)planes)[0] = _latestFrame;
            }

            return _latestFrame;
        }
    }

    private void UnlockVideo(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        lock (_frameSync)
        {
            if (D3DRenderer != null && _latestFrame != IntPtr.Zero && VideoW > 0 && VideoH > 0)
            {
                D3DRenderer.UpdateTexture(_latestFrame, VideoW, VideoH);
            }
        }
    }

    public IntPtr GetLatestBgraFramePointer()
    {
        lock (_frameSync)
        {
            return _latestFrame;
        }
    }

    private void EnsureFrameBuffer(int requiredBytes)
    {
        if (_latestFrame != IntPtr.Zero && requiredBytes <= _frameBytes)
            return;

        if (_latestFrame != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_latestFrame);
            _latestFrame = IntPtr.Zero;
        }

        _latestFrame = Marshal.AllocHGlobal(requiredBytes);
        _frameBytes = requiredBytes;
    }

    private void RecreateD3DRenderer()
    {
        D3DRenderer?.Dispose();
        D3DRenderer = new D3D9Renderer(_hostHwnd, VideoW, VideoH);
    }

    public void Dispose()
    {
        lock (_frameSync)
        {
            try
            {
                _mediaPlayer.Stop();
            }
            catch
            {
            }

            D3DRenderer?.Dispose();
            D3DRenderer = null;

            _media?.Dispose();
            _mediaPlayer.Dispose();
            _libVlc.Dispose();

            if (_latestFrame != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_latestFrame);
                _latestFrame = IntPtr.Zero;
                _frameBytes = 0;
            }
        }
    }
}
