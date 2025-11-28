using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SharpDX;
using SharpDX.Direct3D9;

namespace VideoZoom
{
    public sealed class D3D9Renderer : IDisposable
    {
        private readonly Direct3DEx _d3dContext;
        private readonly DeviceEx _d3dDevice;
        private Texture _renderTexture;
        private Surface _renderSurface;
        private Surface _systemSurface;  // Lockable surface in system memory
        private bool _isDisposed = false;
        private int _updateCount = 0;

        public int TextureWidth { get; private set; }
        public int TextureHeight { get; private set; }

        public D3D9Renderer(int width, int height)
        {
            TextureWidth = width;
            TextureHeight = height;

            var presentParams = new PresentParameters
            {
                Windowed = true,
                SwapEffect = SwapEffect.Discard,
                DeviceWindowHandle = GetDesktopWindow(),
                PresentationInterval = PresentInterval.Default,
            };

            _d3dContext = new Direct3DEx();
            _d3dDevice = new DeviceEx(
                _d3dContext,
                0,
                DeviceType.Hardware,
                IntPtr.Zero,
                CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
                presentParams);

            // Create a render target texture that D3DImage can use
            _renderTexture = new Texture(
                _d3dDevice,
                TextureWidth,
                TextureHeight,
                1,
                Usage.RenderTarget,
                Format.X8R8G8B8,
                Pool.Default);

            _renderSurface = _renderTexture.GetSurfaceLevel(0);

            // Create a lockable system memory surface for uploading texture data
            _systemSurface = Surface.CreateOffscreenPlain(
                _d3dDevice,
                TextureWidth,
                TextureHeight,
                Format.X8R8G8B8,
                Pool.SystemMemory);

            System.Diagnostics.Debug.WriteLine($"D3D9Renderer created: {width}x{height}");
        }

        public void UpdateTexture(IntPtr dataPtr, int width, int height)
        {
            if (_isDisposed || _systemSurface == null || _renderSurface == null || dataPtr == IntPtr.Zero)
            {
                return;
            }

            if (width != TextureWidth || height != TextureHeight)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTexture dimension mismatch: texture={TextureWidth}x{TextureHeight}, data={width}x{height}");
                return;
            }

            DataRectangle dataRect = default;
            bool locked = false;

            try
            {
                // Lock the SYSTEM MEMORY surface (this works!)
                dataRect = _systemSurface.LockRectangle(LockFlags.None);
                locked = true;

                int dstPitch = dataRect.Pitch;
                int srcPitch = width * 4; // 4 bytes per pixel (BGRA)

                // Copy line by line using unsafe pointer arithmetic
                unsafe
                {
                    byte* dst = (byte*)dataRect.DataPointer.ToPointer();
                    byte* src = (byte*)dataPtr.ToPointer();

                    for (int y = 0; y < height; y++)
                    {
                        byte* dstRow = dst + (y * dstPitch);
                        byte* srcRow = src + (y * srcPitch);

                        // Copy one row
                        Buffer.MemoryCopy(srcRow, dstRow, dstPitch, srcPitch);
                    }
                }

                _systemSurface.UnlockRectangle();
                locked = false;

                // Copy from system memory surface to render target surface
                _d3dDevice.UpdateSurface(_systemSurface, _renderSurface);

                _updateCount++;
                if (_updateCount == 1 || _updateCount % 30 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateTexture: {_updateCount} frames processed successfully");
                }
            }
            catch (SharpDXException ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTexture SharpDX error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTexture error: {ex.Message}");
            }
            finally
            {
                if (locked)
                {
                    try
                    {
                        _systemSurface.UnlockRectangle();
                    }
                    catch { }
                }
            }
        }

        public void Render(D3DImage d3dImage)
        {
            if (_isDisposed || d3dImage == null || _renderSurface == null) return;

            try
            {
                d3dImage.Lock();

                if (d3dImage.IsFrontBufferAvailable)
                {
                    d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _renderSurface.NativePointer);

                    if (d3dImage.PixelWidth > 0 && d3dImage.PixelHeight > 0)
                    {
                        d3dImage.AddDirtyRect(new Int32Rect(0, 0, d3dImage.PixelWidth, d3dImage.PixelHeight));
                    }
                }

                d3dImage.Unlock();
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
                try { d3dImage?.Unlock(); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render unexpected error: {ex.Message}");
                try { d3dImage?.Unlock(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _systemSurface?.Dispose();
                _renderSurface?.Dispose();
                _renderTexture?.Dispose();
                _d3dDevice?.Dispose();
                _d3dContext?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
        }

        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr GetDesktopWindow();
    }
}