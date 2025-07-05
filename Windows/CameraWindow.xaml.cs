using System.Windows;
using System.Windows.Media.Imaging;

namespace FigmaToWpf
{
    public partial class CameraWindow : Window
    {
        public CameraWindow(WriteableBitmap cameraBitmap)
        {
            InitializeComponent();
            UpdateImage(cameraBitmap);
            this.Closing += (s, e) => CameraImage.Source = null;
        }

        public void UpdateImage(WriteableBitmap cameraBitmap)
        {
            if (cameraBitmap != null && !cameraBitmap.IsFrozen)
            {
                CameraImage.Source = cameraBitmap;
                CameraImage.Width = cameraBitmap.PixelWidth;
                CameraImage.Height = cameraBitmap.PixelHeight;
            }
        }
    }
}