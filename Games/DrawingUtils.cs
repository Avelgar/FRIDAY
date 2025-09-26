using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Friday.Games
{
    public static class DrawingUtils
    {
        public static Mat CreateTextImage(string text, MCvScalar textColor, MCvScalar backgroundColor, int width = 200, int height = 50)
        {
            // Создаем временное изображение с текстом
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                // Настраиваем качество рендеринга
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Создаем сплошной цвет фона
                System.Drawing.Color bgColor = System.Drawing.Color.FromArgb(
                    255, // Полностью непрозрачный
                    (int)backgroundColor.V2, // R (поменяли порядок для BGR)
                    (int)backgroundColor.V1, // G
                    (int)backgroundColor.V0  // B
                );

                // Заливаем фон
                g.Clear(bgColor);

                // Настраиваем шрифт
                Font font = new Font("Arial", 16, FontStyle.Bold);

                // Настраиваем формат текста
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                // Создаем цвет текста
                System.Drawing.Color txtColor = System.Drawing.Color.FromArgb(
                    255, // Полностью непрозрачный
                    (int)textColor.V2, // R (поменяли порядок для BGR)
                    (int)textColor.V1, // G
                    (int)textColor.V0  // B
                );

                // Рисуем текст
                g.DrawString(text, font, new SolidBrush(txtColor),
                            new RectangleF(0, 0, width, height), format);

                // Конвертируем Bitmap в Mat
                return BitmapToMat(bmp);
            }
        }

        private static Mat BitmapToMat(Bitmap bitmap)
        {
            // Блокируем биты Bitmap для прямого доступа к данным
            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                // Создаем Mat из данных Bitmap
                Mat mat = new Mat(bitmap.Height, bitmap.Width, DepthType.Cv8U, 4, bmpData.Scan0, bmpData.Stride);

                // Конвертируем из BGRA в BGR (убираем альфа-канал)
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
            // Внешний круг (контур) - зеленый
            int radius = 40;
            CvInvoke.Circle(frame, center, radius, new MCvScalar(0, 255, 0), 3); // Зеленый контур

            // Внутренний круг (прогресс) - красный
            if (progress > 0)
            {
                // Рассчитываем угол для дуги прогресса (0-360 градусов)
                int angle = (int)(360 * progress);

                // Рисуем дугу прогресса красным
                CvInvoke.Ellipse(frame, center, new Size(radius, radius), 0, 0, angle, new MCvScalar(0, 0, 255), 5);
            }

            // Центральная точка - красная
            CvInvoke.Circle(frame, center, 5, new MCvScalar(0, 0, 255), -1);
        }
    }
}
