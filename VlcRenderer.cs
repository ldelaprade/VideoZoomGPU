using LibVLCSharp.Shared;
using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace VideoZoom
{
    public sealed class VlcRenderer : IDisposable
    {
        private readonly LibVLC _libVLC;
        private readonly LibVLCSharp.Shared.MediaPlayer _mp;
        private IntPtr _videoBuffer = IntPtr.Zero;
        private IntPtr _convertBuffer = IntPtr.Zero;  // For format conversion

        // Separate buffers for I420 planes
        private IntPtr _yBuffer = IntPtr.Zero;
        private IntPtr _uBuffer = IntPtr.Zero;
        private IntPtr _vBuffer = IntPtr.Zero;

        private int _bufferSize = 0;
        private int _frameCount = 0;
        private string _currentFormat = "RV32";

        public int VideoW { get; private set; }
        public int VideoH { get; private set; }

        public D3D9Renderer D3DRenderer { get; private set; }

        public VlcRenderer()
        {
            Core.Initialize();
            // Force software decoding and specific output format
            _libVLC = new LibVLC(
                "--no-osd",
                "--quiet",
                "--no-video-title-show",
                "--avcodec-hw=none",
                "--vout=dummy",
                "--sout-transcode-vcodec=RV32"  // Try to force RV32 output
            );
            _mp = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            InstallCallbacks();
        }

        public double Position
        {
            get => _mp.Position; // 0.0 to 1.0
            set => _mp.Position = (float)value;
        }

        private void InstallCallbacks()
        {
            _mp.SetVideoFormatCallbacks(VideoFormat, VideoCleanup);
            _mp.SetVideoCallbacks(Lock, Unlock, Display);
        }

        private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            FourCC.FromIntPtr(chroma, out var fourcc);
            System.Diagnostics.Debug.WriteLine($"VideoFormat called: {width}x{height}, format: {fourcc}");

            _currentFormat = fourcc;

            // FORCE RV32 - reject I420
            if (fourcc == "I420")
            {
                System.Diagnostics.Debug.WriteLine($"REJECTING I420, requesting RV32");
                Marshal.Copy(FourCC.ToBytes("RV32"), 0, chroma, 4);
                _currentFormat = "RV32";
            }
            else if (fourcc != "RV32")
            {
                System.Diagnostics.Debug.WriteLine($"Requesting RV32 instead of {fourcc}");
                Marshal.Copy(FourCC.ToBytes("RV32"), 0, chroma, 4);
                _currentFormat = "RV32";
            }

            // Some codecs need specific dimensions (must be even)
            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            VideoW = (int)width;
            VideoH = (int)height;

            if (_currentFormat == "I420")
            {
                // I420 format: Y plane + U plane (1/4 size) + V plane (1/4 size)
                int ySize = VideoW * VideoH;
                int uvSize = (VideoW / 2) * (VideoH / 2);

                // CRITICAL: For I420, VLC expects separate pitches for Y, U, V planes
                // Since we can't modify the ref parameter as an array, we use the first value for Y
                // and VLC will use the standard I420 layout (Y full width, U/V half width)
                pitches = (uint)VideoW;  // Y plane pitch (full width)
                lines = (uint)VideoH;    // Height for Y plane

                System.Diagnostics.Debug.WriteLine($"I420 format: width={VideoW}, height={VideoH}");
                System.Diagnostics.Debug.WriteLine($"I420 pitch={pitches}, lines={lines}");
                System.Diagnostics.Debug.WriteLine($"I420 sizes: Y={ySize}, U={uvSize}, V={uvSize}, total={ySize + uvSize * 2}");

                // Allocate separate buffers for Y, U, V planes
                if (_yBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_yBuffer);
                if (_uBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_uBuffer);
                if (_vBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_vBuffer);

                _yBuffer = Marshal.AllocHGlobal(ySize);
                _uBuffer = Marshal.AllocHGlobal(uvSize);
                _vBuffer = Marshal.AllocHGlobal(uvSize);

                // Initialize U and V buffers to 128 (neutral chroma) - will show grayscale if VLC doesn't write
                unsafe
                {
                    byte* y = (byte*)_yBuffer.ToPointer();
                    byte* u = (byte*)_uBuffer.ToPointer();
                    byte* v = (byte*)_vBuffer.ToPointer();
                    for (int i = 0; i < ySize; i++)
                        y[i] = 0;
                    for (int i = 0; i < uvSize; i++)
                    {
                        u[i] = 128;
                        v[i] = 128;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Allocated Y buffer: {ySize} bytes at {_yBuffer:X}");
                System.Diagnostics.Debug.WriteLine($"Allocated U buffer: {uvSize} bytes at {_uBuffer:X}");
                System.Diagnostics.Debug.WriteLine($"Allocated V buffer: {uvSize} bytes at {_vBuffer:X}");

                // Also allocate conversion buffer
                int bgraSize = VideoW * VideoH * 4;
                if (_convertBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_convertBuffer);
                }
                _convertBuffer = Marshal.AllocHGlobal(bgraSize);
                System.Diagnostics.Debug.WriteLine($"Allocated conversion buffer: {bgraSize} bytes");
            }
            else // RV32
            {
                pitches = (uint)(VideoW * 4); // 4 bytes per pixel (BGRA)
                lines = (uint)VideoH;
                _bufferSize = VideoW * VideoH * 4;

                // Allocate single buffer for packed format
                if (_videoBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_videoBuffer);
                }
                _videoBuffer = Marshal.AllocHGlobal(_bufferSize);

                System.Diagnostics.Debug.WriteLine($"RV32 format: pitch={pitches}, lines={lines}, bufferSize={_bufferSize}");
                System.Diagnostics.Debug.WriteLine($"Allocated video buffer: {_bufferSize} bytes at {_videoBuffer:X}");
            }

            // Create D3D9 renderer - recreate if dimensions changed
            if (D3DRenderer == null || D3DRenderer.TextureWidth != VideoW || D3DRenderer.TextureHeight != VideoH)
            {
                System.Diagnostics.Debug.WriteLine($"Recreating D3D9Renderer from {D3DRenderer?.TextureWidth}x{D3DRenderer?.TextureHeight} to {VideoW}x{VideoH}");
                D3DRenderer?.Dispose();
                D3DRenderer = new D3D9Renderer(VideoW, VideoH);
                System.Diagnostics.Debug.WriteLine($"Created D3D9Renderer: {VideoW}x{VideoH}");
            }

            return 1;
        }

        private void VideoCleanup(ref IntPtr opaque)
        {
            System.Diagnostics.Debug.WriteLine("VideoCleanup called");

            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
                _videoBuffer = IntPtr.Zero;
            }

            // Y, U, V buffers are just pointers into _videoBuffer, don't free them separately
            _yBuffer = IntPtr.Zero;
            _uBuffer = IntPtr.Zero;
            _vBuffer = IntPtr.Zero;

            if (_convertBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_convertBuffer);
                _convertBuffer = IntPtr.Zero;
            }

            D3DRenderer?.Dispose();
            D3DRenderer = null;
            VideoW = 0;
            VideoH = 0;
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            // VLC expects an array of plane pointers for planar formats like I420
            if (_currentFormat == "I420")
            {
                // I420 has 3 planes: Y, U, V - provide pointers to each plane
                unsafe
                {
                    IntPtr* planeArray = (IntPtr*)planes.ToPointer();
                    planeArray[0] = _yBuffer;  // Y plane
                    planeArray[1] = _uBuffer;  // U plane
                    planeArray[2] = _vBuffer;  // V plane

                    if (_frameCount == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Lock callback - providing plane pointers:");
                        System.Diagnostics.Debug.WriteLine($"  Y plane: {_yBuffer:X}");
                        System.Diagnostics.Debug.WriteLine($"  U plane: {_uBuffer:X}");
                        System.Diagnostics.Debug.WriteLine($"  V plane: {_vBuffer:X}");
                    }
                }
            }
            else if (_videoBuffer != IntPtr.Zero)
            {
                // For packed formats like RV32, just one plane
                Marshal.WriteIntPtr(planes, _videoBuffer);
            }
            return IntPtr.Zero;
        }

        private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            // Frame has been written to buffer, now copy to D3D texture
            if (D3DRenderer != null && VideoW > 0 && VideoH > 0)
            {
                try
                {
                    _frameCount++;

                    if (_currentFormat == "I420")
                    {
                        // Debug: Check if VLC actually wrote to U and V buffers
                        if (_frameCount == 1)
                        {
                            unsafe
                            {
                                byte* u = (byte*)_uBuffer.ToPointer();
                                byte* v = (byte*)_vBuffer.ToPointer();
                                System.Diagnostics.Debug.WriteLine($"After VLC write - U[0]={u[0]}, U[100]={u[100]}, U[1000]={u[1000]}, V[0]={v[0]}, V[100]={v[100]}, V[1000]={v[1000]}");

                                // Count non-128 values
                                int nonNeutralU = 0, nonNeutralV = 0;
                                int uvSize = (VideoW / 2) * (VideoH / 2);
                                for (int i = 0; i < uvSize; i++)
                                {
                                    if (u[i] != 128) nonNeutralU++;
                                    if (v[i] != 128) nonNeutralV++;
                                }
                                System.Diagnostics.Debug.WriteLine($"Non-128 chroma values: U={nonNeutralU}/{uvSize}, V={nonNeutralV}/{uvSize}");
                            }
                        }

                        // Convert I420 to BGRA from separate plane buffers
                        YuvConverter.I420ToBGRA_Planar(_yBuffer, _uBuffer, _vBuffer, _convertBuffer, VideoW, VideoH);
                        D3DRenderer.UpdateTexture(_convertBuffer, VideoW, VideoH);
                    }
                    else if (_videoBuffer != IntPtr.Zero)
                    {
                        // Already BGRA, use directly
                        D3DRenderer.UpdateTexture(_videoBuffer, VideoW, VideoH);
                    }

                    if (_frameCount == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"First frame: format={_currentFormat}, size={VideoW}x{VideoH}");
                    }
                    if (_frameCount % 30 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Unlock: Processing frame {_frameCount}, format={_currentFormat}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unlock error: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            // Display callback - frame is ready to be shown
            // Actual rendering happens in OnRendering loop
        }

        public void Open(string path)
        {
            _mp.Stop();
            _mp.Media?.Dispose();
            var media = new Media(_libVLC, new Uri(path));
            _mp.Media = media;
        }

        public void Play() => _mp.Play();
        public void Pause() => _mp.Pause();
        public void Stop() => _mp.Stop();

        public void Dispose()
        {
            try
            {
                _mp?.Stop();
            }
            catch { /* ignore */ }

            _mp?.Media?.Dispose();
            _mp?.Dispose();
            _libVLC?.Dispose();

            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
                _videoBuffer = IntPtr.Zero;
            }

            // Y, U, V buffers are just pointers into _videoBuffer, don't free them separately
            _yBuffer = IntPtr.Zero;
            _uBuffer = IntPtr.Zero;
            _vBuffer = IntPtr.Zero;

            if (_convertBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_convertBuffer);
                _convertBuffer = IntPtr.Zero;
            }

            D3DRenderer?.Dispose();
        }
    }

    internal static class FourCC
    {
        public static void FromIntPtr(IntPtr chroma, out string fourcc)
        {
            byte[] bytes = new byte[4];
            Marshal.Copy(chroma, bytes, 0, 4);
            fourcc = System.Text.Encoding.ASCII.GetString(bytes);
        }

        public static byte[] ToBytes(string fourcc)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(fourcc);
            if (bytes.Length != 4)
                throw new ArgumentException("FourCC must be 4 characters.");
            return bytes;
        }
    }
}