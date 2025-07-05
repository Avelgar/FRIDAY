using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Forms;
using Tesseract;
using Newtonsoft.Json;
using System.Text;

namespace Friday.Services
{
    public class ScreenshotForm : Form
    {
        public static bool IsActive { get; private set; } = false;

        private Point startPoint;
        private Rectangle selectionRectangle;
        private bool isSelecting;
        public bool IsCancelled { get; private set; } = true;
        public event Action<string> OnMessageReceived;

        private readonly VoiceService _voiceService;

        public ScreenshotForm(VoiceService voiceService)
        {
            _voiceService = voiceService;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.Opacity = 0.5;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;

            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.Paint += OnPaint;

            this.Shown += (sender, e) => IsActive = true;
            this.FormClosed += (sender, e) => IsActive = false;
            this.VisibleChanged += (sender, e) => IsActive = this.Visible;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            isSelecting = true;
            startPoint = e.Location;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                selectionRectangle = new Rectangle(Math.Min(startPoint.X, e.X),
                                                   Math.Min(startPoint.Y, e.Y),
                                                   Math.Abs(startPoint.X - e.X),
                                                   Math.Abs(startPoint.Y - e.Y));
                this.Invalidate(); // Перерисовать форму
            }
        }

        public class ScreenshotResponse
        {
            [JsonProperty("screenshot_response")]
            public string ResponseType { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }

            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            isSelecting = false;

            if (selectionRectangle.Width == 0 || selectionRectangle.Height == 0)
            {
                IsCancelled = true; // Если область не выбрана, отменяем действие
                this.Close();
                return;
            }

            IsCancelled = false; // Действие не отменено
            this.Hide(); // Скрыть форму
            CaptureScreen(selectionRectangle); // Захватить экран в выделенной области
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (isSelecting)
            {
                e.Graphics.DrawRectangle(Pens.Red, selectionRectangle); // Рисуем рамку выделенной области
            }
        }

        private async Task CaptureScreen(Rectangle bounds)
        {
            Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(screenshot))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            string screenshotPath = "screenshot.png";
            screenshot.Save(screenshotPath);

            string recognizedText = ProcessScreenshot(screenshotPath);
            OnMessageReceived?.Invoke($"Распознанный текст: {recognizedText}");
            string text = $"Тебе прислали текст со скриншота: {recognizedText}. Это может быть задача или вопрос из теста, в любом случае постарайся дать ответ. Учти, что твой ответ будет озвучен, так что не добалвяй дополнительные символы в текст и дай ответ без переноса текста на следующую строку.";
            try
            {
                var message = new
                {
                    screenshot_text = "screenshot_text",
                    text = text,
                    timestamp = DateTime.Now
                };

                string json = JsonConvert.SerializeObject(message);

                string response = await GeminiService.GenerateTextAsync(json);

                // Обработка ответа
                if (!string.IsNullOrEmpty(response))
                {
                    await _voiceService.SpeakAsync(response);
                }
                else
                {
                    MessageBox.Show("Не удалось получить ответ от Gemini", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при отправке текста: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Close();
            }
        }

        private string ProcessScreenshot(string imagePath)
        {
            string recognizedText = string.Empty;

            string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\tessdata");


            using (var engine = new TesseractEngine(tessdataPath, "rus+eng", EngineMode.Default))
            {
                using (var img = Pix.LoadFromFile(imagePath))
                {
                    using (var page = engine.Process(img))
                    {
                        recognizedText = page.GetText();
                    }
                }
            }

            return recognizedText;
        }
    }
}
