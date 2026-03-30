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
        private PresentParameters _presentParams;
        private Texture _renderTexture;
        private Surface _renderSurface;
        private Surface _systemSurface;
        private bool _isDisposed = false;
        private int _updateCount = 0;

        public int TextureWidth { get; private set; }
        public int TextureHeight { get; private set; }

        public D3D9Renderer(IntPtr hwnd, int width, int height)
        {
            TextureWidth = width;
            TextureHeight = height;

            _presentParams = new PresentParameters
            {
                Windowed = true,
                SwapEffect = SwapEffect.Discard,
                DeviceWindowHandle = hwnd,
                PresentationInterval = PresentInterval.Default,
            };

            _d3dContext = new Direct3DEx();

            var flags = CreateFlags.Multithreaded | CreateFlags.FpuPreserve | CreateFlags.HardwareVertexProcessing;
            try
            {
                _d3dDevice = new DeviceEx(_d3dContext, 0, DeviceType.Hardware, hwnd, flags, _presentParams);
            }
            catch
            {
                flags = CreateFlags.Multithreaded | CreateFlags.FpuPreserve | CreateFlags.SoftwareVertexProcessing;
                _d3dDevice = new DeviceEx(_d3dContext, 0, DeviceType.Hardware, hwnd, flags, _presentParams);
            }

            CreateResources();

            System.Diagnostics.Debug.WriteLine($"D3D9Renderer created: {width}x{height}");
        }

        // Recreate textures/surfaces after ResetEx or device lost
        private void CreateResources()
        {
            DisposeResourcesOnly();

            _renderTexture = new Texture(
                _d3dDevice,
                TextureWidth,
                TextureHeight,
                1,
                Usage.RenderTarget,
                Format.X8R8G8B8,
                Pool.Default);

            _renderSurface = _renderTexture.GetSurfaceLevel(0);

            _systemSurface = Surface.CreateOffscreenPlain(
                _d3dDevice,
                TextureWidth,
                TextureHeight,
                Format.X8R8G8B8,
                Pool.SystemMemory);
        }

        public void SetWindowHandle(IntPtr hwnd)
        {
            if (_isDisposed) return;
            try
            {
                if (_presentParams.DeviceWindowHandle == hwnd) return;
                _presentParams.DeviceWindowHandle = hwnd;
                _d3dDevice.ResetEx(ref _presentParams, null);
                CreateResources(); // IMPORTANT: recreate surfaces after ResetEx
                System.Diagnostics.Debug.WriteLine("D3D9Renderer window handle updated and resources recreated.");
            }
            catch (SharpDXException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetWindowHandle error: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetWindowHandle general error: {ex.Message}");
            }
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
                dataRect = _systemSurface.LockRectangle(LockFlags.None);
                locked = true;

                int dstPitch = dataRect.Pitch;
                int srcPitch = width * 4;

                unsafe
                {
                    byte* dst = (byte*)dataRect.DataPointer.ToPointer();
                    byte* src = (byte*)dataPtr.ToPointer();

                    for (int y = 0; y < height; y++)
                    {
                        byte* dstRow = dst + (y * dstPitch);
                        byte* srcRow = src + (y * srcPitch);
                        Buffer.MemoryCopy(srcRow, dstRow, dstPitch, srcPitch);
                    }
                }

                _systemSurface.UnlockRectangle();
                locked = false;

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
                    try { _systemSurface.UnlockRectangle(); } catch { }
                }
            }
        }

        public void Render(D3DImage d3dImage)
        {
            if (_isDisposed || d3dImage == null || _renderSurface == null) return;
            if (!d3dImage.IsFrontBufferAvailable) return;

            try
            {
                d3dImage.Lock();
                d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _renderSurface.NativePointer);

                if (d3dImage.PixelWidth > 0 && d3dImage.PixelHeight > 0)
                {
                    d3dImage.AddDirtyRect(new Int32Rect(0, 0, d3dImage.PixelWidth, d3dImage.PixelHeight));
                }
                d3dImage.Unlock();
            }
            catch (SharpDXException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render SharpDX error: {ex.Message}");
                try
                {
                    var result = _d3dDevice.TestCooperativeLevel();
                    if (result == ResultCode.DeviceLost || result == ResultCode.DeviceNotReset)
                    {
                        System.Diagnostics.Debug.WriteLine("Device lost; recreating resources.");
                        CreateResources();
                    }
                }
                catch { }
                try { d3dImage?.Unlock(); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render general error: {ex.Message}");
                try { d3dImage?.Unlock(); } catch { }
            }
        }

        private void DisposeResourcesOnly()
        {
            try
            {
                _systemSurface?.Dispose();
                _renderSurface?.Dispose();
                _renderTexture?.Dispose();
            }
            catch { }
            _systemSurface = null;
            _renderSurface = null;
            _renderTexture = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                DisposeResourcesOnly();
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