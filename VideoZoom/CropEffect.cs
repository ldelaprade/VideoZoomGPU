using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VideoZoom
{
    public class CropEffect : ShaderEffect
    {
        private static readonly PixelShader _pixelShader;

        static CropEffect()
        {
            try
            {
                // If file is at project root and Build Action=Resource
                var uri = new Uri("/VideoZoom;component/CropEffect.ps", UriKind.Relative);
                _pixelShader = new PixelShader { UriSource = uri };
            }
            catch (Exception ex)
            {
                // Log then rethrow to see the root cause
                MessageBox.Show("Could not load CropEffect.ps shader resource. Ensure Build Action = Resource.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"CropEffect init failed: {ex}");
                throw;
            }
        }

        public CropEffect()
        {
            PixelShader = _pixelShader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(CropXProperty);
            UpdateShaderValue(CropYProperty);
            UpdateShaderValue(CropWidthProperty);
            UpdateShaderValue(CropHeightProperty);
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(CropEffect), 0);

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        // Register four separate float properties for the crop rectangle
        public static readonly DependencyProperty CropXProperty =
            DependencyProperty.Register("CropX", typeof(double), typeof(CropEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public static readonly DependencyProperty CropYProperty =
            DependencyProperty.Register("CropY", typeof(double), typeof(CropEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));

        public static readonly DependencyProperty CropWidthProperty =
            DependencyProperty.Register("CropWidth", typeof(double), typeof(CropEffect),
                new UIPropertyMetadata(1.0, PixelShaderConstantCallback(2)));

        public static readonly DependencyProperty CropHeightProperty =
            DependencyProperty.Register("CropHeight", typeof(double), typeof(CropEffect),
                new UIPropertyMetadata(1.0, PixelShaderConstantCallback(3)));

        public double CropX
        {
            get => (double)GetValue(CropXProperty);
            set => SetValue(CropXProperty, value);
        }

        public double CropY
        {
            get => (double)GetValue(CropYProperty);
            set => SetValue(CropYProperty, value);
        }

        public double CropWidth
        {
            get => (double)GetValue(CropWidthProperty);
            set => SetValue(CropWidthProperty, value);
        }

        public double CropHeight
        {
            get => (double)GetValue(CropHeightProperty);
            set => SetValue(CropHeightProperty, value);
        }

        // Convenience property to set all four values at once
        public Rect CropRectangle
        {
            get => new Rect(CropX, CropY, CropWidth, CropHeight);
            set
            {
                CropX = value.X;
                CropY = value.Y;
                CropWidth = value.Width;
                CropHeight = value.Height;
            }
        }
    }
}