using System;
using System.Runtime.InteropServices;

namespace VideoZoom
{
    public static class YuvConverter
    {
        private static int _callCount = 0;

        /// <summary>
        /// Converts I420 (YUV420P) format to X8R8G8B8 format from separate plane buffers
        /// </summary>
        public static void I420ToBGRA_Planar(IntPtr yBuffer, IntPtr uBuffer, IntPtr vBuffer, IntPtr bgraBuffer, int width, int height)
        {
            int ySize = width * height;
            int uvSize = (width / 2) * (height / 2);

            unsafe
            {
                byte* yPlane = (byte*)yBuffer.ToPointer();
                byte* uPlane = (byte*)uBuffer.ToPointer();
                byte* vPlane = (byte*)vBuffer.ToPointer();
                byte* dst = (byte*)bgraBuffer.ToPointer();

                // Debug first frame only
                if (_callCount == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"=== YUV CONVERSION DEBUG (PLANAR) ===");
                    System.Diagnostics.Debug.WriteLine($"Separate buffers: Y={yBuffer:X}, U={uBuffer:X}, V={vBuffer:X}");
                    System.Diagnostics.Debug.WriteLine($"Buffer sizes: ySize={ySize}, uvSize={uvSize}");
                    System.Diagnostics.Debug.WriteLine($"Y plane samples: Y[0]={yPlane[0]}, Y[100]={yPlane[100]}, Y[1000]={yPlane[1000]}");
                    System.Diagnostics.Debug.WriteLine($"U plane samples: U[0]={uPlane[0]}, U[10]={uPlane[10]}, U[100]={uPlane[100]}");
                    System.Diagnostics.Debug.WriteLine($"V plane samples: V[0]={vPlane[0]}, V[10]={vPlane[10]}, V[100]={vPlane[100]}");
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int yIndex = y * width + x;
                        int uvIndex = (y / 2) * (width / 2) + (x / 2);

                        // Get YUV values
                        int Y = yPlane[yIndex];
                        int U = uPlane[uvIndex];
                        int V = vPlane[uvIndex];

                        // Apply standard YUV to RGB conversion (BT.709)
                        int C = Y - 16;
                        int D = U - 128;
                        int E = V - 128;

                        int r = (298 * C + 409 * E + 128) >> 8;
                        int g = (298 * C - 100 * D - 208 * E + 128) >> 8;
                        int b = (298 * C + 516 * D + 128) >> 8;

                        // Clamp values to valid range
                        r = r < 0 ? 0 : (r > 255 ? 255 : r);
                        g = g < 0 ? 0 : (g > 255 ? 255 : g);
                        b = b < 0 ? 0 : (b > 255 ? 255 : b);

                        // Debug sample pixel conversion on first call
                        if (_callCount == 0 && yIndex == 1000)
                        {
                            System.Diagnostics.Debug.WriteLine($"Sample pixel 1000: Y={Y}, U={U}, V={V} -> R={r}, G={g}, B={b}");
                        }

                        // Write in X8R8G8B8 format: BGRX byte order
                        int dstIndex = yIndex * 4;
                        dst[dstIndex + 0] = (byte)b;  // Blue
                        dst[dstIndex + 1] = (byte)g;  // Green  
                        dst[dstIndex + 2] = (byte)r;  // Red
                        dst[dstIndex + 3] = 0;        // X (padding)
                    }
                }

                _callCount++;
                if (_callCount == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"=== END YUV CONVERSION DEBUG ===");
                }
            }
        }

        /// <summary>
        /// Converts I420 (YUV420P) format to X8R8G8B8 format (BGRX byte order)
        /// </summary>
        public static void I420ToBGRA(IntPtr i420Buffer, IntPtr bgraBuffer, int width, int height)
        {
            int ySize = width * height;
            int uvSize = ySize / 4;

            unsafe
            {
                byte* src = (byte*)i420Buffer.ToPointer();
                byte* dst = (byte*)bgraBuffer.ToPointer();

                byte* yPlane = src;
                byte* uPlane = src + ySize;
                byte* vPlane = src + ySize + uvSize;

                // Debug first frame only
                if (_callCount == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"=== YUV CONVERSION DEBUG ===");
                    System.Diagnostics.Debug.WriteLine($"Buffer layout: ySize={ySize}, uvSize={uvSize}, total={ySize + uvSize * 2}");
                    System.Diagnostics.Debug.WriteLine($"Expected buffer size: {ySize + uvSize * 2}");
                    System.Diagnostics.Debug.WriteLine($"Y plane samples: Y[0]={yPlane[0]}, Y[100]={yPlane[100]}, Y[1000]={yPlane[1000]}");
                    System.Diagnostics.Debug.WriteLine($"U plane samples: U[0]={uPlane[0]}, U[10]={uPlane[10]}, U[100]={uPlane[100]}");
                    System.Diagnostics.Debug.WriteLine($"V plane samples: V[0]={vPlane[0]}, V[10]={vPlane[10]}, V[100]={vPlane[100]}");
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int yIndex = y * width + x;
                        int uvIndex = (y / 2) * (width / 2) + (x / 2);

                        // Get YUV values
                        int Y = yPlane[yIndex];
                        int U = uPlane[uvIndex];
                        int V = vPlane[uvIndex];

                        // Apply standard YUV to RGB conversion (BT.709)
                        int C = Y - 16;
                        int D = U - 128;
                        int E = V - 128;

                        int r = (298 * C + 409 * E + 128) >> 8;
                        int g = (298 * C - 100 * D - 208 * E + 128) >> 8;
                        int b = (298 * C + 516 * D + 128) >> 8;

                        // Clamp values to valid range
                        r = r < 0 ? 0 : (r > 255 ? 255 : r);
                        g = g < 0 ? 0 : (g > 255 ? 255 : g);
                        b = b < 0 ? 0 : (b > 255 ? 255 : b);

                        // Debug sample pixel conversion on first call
                        if (_callCount == 0 && yIndex == 1000)
                        {
                            System.Diagnostics.Debug.WriteLine($"Sample pixel 1000: Y={Y}, U={U}, V={V} -> R={r}, G={g}, B={b}");
                        }

                        // Write in X8R8G8B8 format: BGRX byte order
                        int dstIndex = yIndex * 4;
                        dst[dstIndex + 0] = (byte)b;  // Blue
                        dst[dstIndex + 1] = (byte)g;  // Green  
                        dst[dstIndex + 2] = (byte)r;  // Red
                        dst[dstIndex + 3] = 0;        // X (padding)
                    }
                }

                _callCount++;
                if (_callCount == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"=== END YUV CONVERSION DEBUG ===");
                }
            }
        }
    }
}
