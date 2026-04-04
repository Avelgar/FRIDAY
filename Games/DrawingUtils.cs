using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;
using System.Drawing.Imaging;

namespace Friday.Games
{
    public static class DrawingUtils
    {
        public static Mat CreateTextImage(string text, MCvScalar textColor, MCvScalar backgroundColor, int width = 200, int height = 50)
        {
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                System.Drawing.Color bgColor = System.Drawing.Color.FromArgb(
                    255,
                    (int)backgroundColor.V2,
                    (int)backgroundColor.V1,
                    (int)backgroundColor.V0
                );

                g.Clear(bgColor);

                Font font = new Font("Arial", 16, FontStyle.Bold);

                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                System.Drawing.Color txtColor = System.Drawing.Color.FromArgb(
                    255,
                    (int)textColor.V2,
                    (int)textColor.V1,
                    (int)textColor.V0
                );

                g.DrawString(text, font, new SolidBrush(txtColor),
                            new RectangleF(0, 0, width, height), format);

                return BitmapToMat(bmp);
            }
        }

        private static Mat BitmapToMat(Bitmap bitmap)
        {
            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                Mat mat = new Mat(bitmap.Height, bitmap.Width, DepthType.Cv8U, 4, bmpData.Scan0, bmpData.Stride);

                Mat result = new Mat();
                CvInvoke.CvtColor(mat, result, ColorConversion.Bgra2Bgr);

                return result;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        public static void DrawProgressCircle(Mat frame, System.Drawing.Point center, float progress)
        {
            int radius = 40;
            CvInvoke.Circle(frame, center, radius, new MCvScalar(0, 255, 0), 3); // Зеленый контур

            if (progress > 0)
            {
                int angle = (int)(360 * progress);

                CvInvoke.Ellipse(frame, center, new Size(radius, radius), 0, 0, angle, new MCvScalar(0, 0, 255), 5);
            }

            CvInvoke.Circle(frame, center, 5, new MCvScalar(0, 0, 255), -1);
        }
    }
}
