// ScreenshotForm.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Friday.Services
{
    public class ScreenshotForm : Form
    {
        public static bool IsActive { get; private set; } = false;

        // Свойство для хранения результата
        public byte[] CapturedImageBytes { get; private set; }

        public bool IsCancelled { get; private set; } = true;

        private Point startPoint;
        private Rectangle selectionRectangle;
        private bool isSelecting;
        private Bitmap _backgroundBitmap;

        public ScreenshotForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.DoubleBuffered = true; // Для плавной отрисовки

            // Делаем скриншот всего экрана один раз при открытии
            CaptureFullScreen();
            this.BackgroundImage = _backgroundBitmap;

            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.Paint += OnPaint;
            this.KeyDown += OnKeyDown; // Добавляем обработчик нажатия клавиш

            this.Shown += (sender, e) => IsActive = true;
            this.FormClosed += (sender, e) => IsActive = false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Закрываем форму по нажатию Esc
            if (e.KeyCode == Keys.Escape)
            {
                IsCancelled = true;
                this.Close();
            }
        }

        private void CaptureFullScreen()
        {
            // Объединяем все экраны в один прямоугольник
            Rectangle totalScreenBounds = SystemInformation.VirtualScreen;
            _backgroundBitmap = new Bitmap(totalScreenBounds.Width, totalScreenBounds.Height);

            using (Graphics g = Graphics.FromImage(_backgroundBitmap))
            {
                // Копируем изображение всего рабочего стола
                g.CopyFromScreen(totalScreenBounds.Location, Point.Empty, totalScreenBounds.Size);
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSelecting = true;
                startPoint = e.Location;
                selectionRectangle = new Rectangle(e.Location, new Size(0, 0));
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                selectionRectangle = new Rectangle(
                    Math.Min(startPoint.X, e.X),
                    Math.Min(startPoint.Y, e.Y),
                    Math.Abs(startPoint.X - e.X),
                    Math.Abs(startPoint.Y - e.Y));

                this.Invalidate(); // Перерисовать форму
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSelecting = false;

                // Если область слишком маленькая, считаем отмененным
                if (selectionRectangle.Width < 5 || selectionRectangle.Height < 5)
                {
                    IsCancelled = true;
                }
                else
                {
                    IsCancelled = false;
                    CaptureSelectedArea();
                }

                this.Close();
            }
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            // Затемняем все, что не выделено
            using (Brush dimBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                // Создаем регион для затемнения
                Region region = new Region(this.ClientRectangle);
                region.Exclude(selectionRectangle);
                e.Graphics.FillRegion(dimBrush, region);
            }

            // Рисуем рамку выделенной области
            if (isSelecting && selectionRectangle.Width > 0 && selectionRectangle.Height > 0)
            {
                e.Graphics.DrawRectangle(Pens.Red, selectionRectangle);
            }
        }

        private void CaptureSelectedArea()
        {
            // Вырезаем выбранную область из фонового скриншота
            using (Bitmap capturedBitmap = new Bitmap(selectionRectangle.Width, selectionRectangle.Height))
            {
                using (Graphics g = Graphics.FromImage(capturedBitmap))
                {
                    // Копируем нужный кусок из полного скриншота
                    Rectangle sourceRect = new Rectangle(selectionRectangle.Location, selectionRectangle.Size);
                    g.DrawImage(_backgroundBitmap, 0, 0, sourceRect, GraphicsUnit.Pixel);
                }

                // Сохраняем результат в байтовый массив
                using (MemoryStream ms = new MemoryStream())
                {
                    capturedBitmap.Save(ms, ImageFormat.Png);
                    CapturedImageBytes = ms.ToArray();
                }
            }
        }

        // Освобождаем ресурсы при закрытии формы
        protected override void Dispose(bool disposing)
        {
            if (disposing && (_backgroundBitmap != null))
            {
                _backgroundBitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}