using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Forms;
using System.Timers;
using System.Text;
using System.Drawing.Imaging;

namespace Friday.Games
{
    public class PoseTrackingService : IDisposable
    {
        private WriteableBitmap _cameraBitmap;
        public WriteableBitmap CameraBitmap => _cameraBitmap;
        public event Action<WriteableBitmap> OnImageUpdated;
        private readonly InferenceSession _session;
        private VideoCapture _camera;
        private const int InputSize = 192;
        private bool _isTracking;
        private AppState _currentState = AppState.MainMenu;
        private int _bestScore = 0;
        private List<GameZone> _game1Buttons = new List<GameZone>();

        private BoxingGame _boxingGame;
        private DodgeGame _dodgeGame;
        private AppearanceGame _appearanceGame;
        private GameBase _currentGame;

        // Размеры экрана
        private readonly int _screenWidth;
        private readonly int _screenHeight;

        // Связи между точками для отрисовки скелета (пары индексов)
        private static readonly (int, int)[] SkeletonConnections = new[]
        {
            // Голова
            (0, 1), (0, 2), (1, 3), (2, 4),
            // Туловище
            (5, 6), (5, 11), (6, 12), (11, 12),
            // Руки
            (5, 7), (7, 9), (6, 8), (8, 10),
            // Ноги
            (11, 13), (13, 15), (12, 14), (14, 16)
        };

        // Фиолетовый цвет для фона (BGR format)
        private readonly MCvScalar PurpleBackground = new MCvScalar(238, 130, 238);
        // Цвета для скелета
        private readonly MCvScalar SkeletonColor = new MCvScalar(255, 255, 255); // Белый
        private readonly MCvScalar PointColor = new MCvScalar(0, 255, 255); // Желтый

        // Игровые зоны
        private List<GameZone> _gameZones;
        private System.Timers.Timer _hoverTimer;
        private DateTime _lastUpdateTime;

        public PoseTrackingService()
        {
            // Получаем размеры экрана
            _screenWidth = Screen.PrimaryScreen.Bounds.Width;
            _screenHeight = Screen.PrimaryScreen.Bounds.Height;

            // Создаем игровые зоны
            CreateGameZones();

            // Настраиваем таймер для обработки наведения
            _hoverTimer = new System.Timers.Timer(16); // ~60 FPS
            _hoverTimer.Elapsed += ProcessHoverProgress;
            _hoverTimer.AutoReset = true;

            _lastUpdateTime = DateTime.Now;

            string modelPath = GetModelPath();
            try
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                _session = new InferenceSession(modelPath, sessionOptions);

                _camera = new VideoCapture(0);
                if (!_camera.IsOpened)
                {
                    System.Windows.MessageBox.Show("Camera initialization failed");
                    throw new Exception("Camera initialization failed");
                }

                // Создаем bitmap с размером экрана
                _cameraBitmap = new WriteableBitmap(
                    _screenWidth,
                    _screenHeight,
                    96, 96,
                    PixelFormats.Bgr24,
                    null);

                _boxingGame = new BoxingGame(_screenWidth, _screenHeight);
                _dodgeGame = new DodgeGame(_screenWidth, _screenHeight);
                _appearanceGame = new AppearanceGame(_screenWidth, _screenHeight);

                // Подписываемся на события возврата в главное меню
                _boxingGame.OnReturnToMainMenu += ReturnToMainMenu;
                _dodgeGame.OnReturnToMainMenu += ReturnToMainMenu;
                _appearanceGame.OnReturnToMainMenu += ReturnToMainMenu;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Init error: {ex}");
                Dispose();
                throw;
            }
        }

        private void CreateGameZones()
        {
            _gameZones = new List<GameZone>
            {
                new GameZone
                {
                    Area = new RectangleF(_screenWidth * 0.1f, _screenHeight * 0.1f, 150, 150),
                    GameName = "Бокс",
                    Color = new MCvScalar(255, 0, 0),
                    HoverProgress = 0
                },
                new GameZone
                {
                    Area = new RectangleF(_screenWidth * 0.5f, _screenHeight * 0.1f, 150, 150),
                    GameName = "Уворачивания",
                    Color = new MCvScalar(0, 255, 0),
                    HoverProgress = 0
                },
                new GameZone
                {
                    Area = new RectangleF(_screenWidth * 0.8f, _screenHeight * 0.1f, 150, 150),
                    GameName = "Изменение внешности",
                    Color = new MCvScalar(0, 0, 255),
                    HoverProgress = 0
                }
            };
        }

        private void ReturnToMainMenu()
        {
            _currentState = AppState.MainMenu;
            _currentGame = null;

            // Сбрасываем прогресс наведения для всех игровых зон
            foreach (var zone in _gameZones)
            {
                zone.HoverProgress = 0;
                zone.HoverStartTime = null;
                zone.IsHovered = false;
                zone.IsLoading = false;
            }
        }


        private string GetModelPath()
        {
            string basePath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\"));
            return Path.Combine(basePath, "Assets", "Models",
                "movenet_singlepose_lightning_4.onnx");
        }

        public void StartTracking()
        {
            if (_isTracking || _camera == null) return;
            _isTracking = true;
            _hoverTimer.Start();

            Task.Run(() =>
            {
                while (_isTracking)
                {
                    using (var frame = new Mat())
                    {
                        if (!_camera.Read(frame) || frame.IsEmpty)
                        {
                            continue;
                        }

                        CvInvoke.Flip(frame, frame, FlipType.Horizontal);

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var keypoints = ProcessFrame(frame);

                            using (Mat purpleFrame = new Mat(new System.Drawing.Size(_screenWidth, _screenHeight), DepthType.Cv8U, 3))
                            {
                                if (_currentState == AppState.MainMenu)
                                {
                                    // Отрисовываем главное меню
                                    DrawMainMenu(purpleFrame, keypoints);
                                }
                                else if (_currentGame != null)
                                {
                                    // Делегируем отрисовку текущей игре
                                    _currentGame.DrawInterface(purpleFrame);

                                    if (keypoints != null)
                                    {
                                        DrawSkeleton(purpleFrame, keypoints);
                                        _currentGame.CheckHandInteraction(keypoints);
                                    }
                                }

                                UpdateCameraImage(purpleFrame);
                            }
                        });

                        Thread.Sleep(30);
                    }
                }
            });
        }

        private void ProcessHoverProgress(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_currentState == AppState.MainMenu)
                {
                    // Обрабатываем прогресс наведения на игровые зоны
                    foreach (var zone in _gameZones)
                    {
                        if (zone.IsHovered)
                        {
                            if (!zone.HoverStartTime.HasValue)
                            {
                                zone.HoverStartTime = DateTime.Now;
                            }

                            // Увеличиваем прогресс (2 секунды для полной загрузки)
                            zone.HoverProgress = Math.Min(1.0f, zone.HoverProgress + deltaTime / 2.0f);

                            // Если прогресс достиг 100%, переходим в игру
                            if (zone.HoverProgress >= 1.0f && !zone.IsLoading)
                            {
                                zone.IsLoading = true;

                                if (zone.GameName == "Бокс")
                                {
                                    _currentState = AppState.BoxingGame;
                                    _currentGame = _boxingGame;
                                }
                                else if (zone.GameName == "Уворачивания")
                                {
                                    _currentState = AppState.DodgeGame;
                                    _currentGame = _dodgeGame;
                                }
                                else if (zone.GameName == "Изменение внешности")
                                {
                                    _currentState = AppState.AppearanceGame;
                                    _currentGame = _appearanceGame;
                                }

                                // Сбрасываем прогресс
                                zone.HoverProgress = 0;
                                zone.HoverStartTime = null;
                                zone.IsLoading = false;
                            }
                        }
                        else
                        {
                            // Если не наведено, сбрасываем прогресс
                            zone.HoverProgress = 0;
                            zone.HoverStartTime = null;
                        }
                    }
                }
                else if (_currentGame != null)
                {
                    // Делегируем обработку текущей игре
                    _currentGame.ProcessHover(deltaTime);
                }
            });
        }

        public void DrawProgressCircle(Mat frame, System.Drawing.Point center, float progress)
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
                CvInvoke.Ellipse(frame, center, new System.Drawing.Size(radius, radius), 0, 0, angle, new MCvScalar(0, 0, 255), 5);
            }

            // Центральная точка - красная
            CvInvoke.Circle(frame, center, 5, new MCvScalar(0, 0, 255), -1);
        }

        private bool IsPointInZone(System.Drawing.PointF point, GameZone zone)
        {
            // Преобразуем нормализованные координаты в пиксельные
            var pixelPoint = new System.Drawing.Point(
                (int)(point.X * _screenWidth),
                (int)(point.Y * _screenHeight)
            );

            return zone.Area.Contains(pixelPoint);
        }

        public Mat CreateTextImage(string text, MCvScalar textColor, MCvScalar backgroundColor, int width = 200, int height = 50)
        {
            // Создаем временное изображение с текстом
            using (Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
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
                Font font = new Font("Arial", 16, System.Drawing.FontStyle.Bold);

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

        private Mat BitmapToMat(Bitmap bitmap)
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

        private List<PointF> ProcessFrame(Mat frame)
        {
            try
            {
                using var resized = new Mat();
                CvInvoke.Resize(frame, resized, new System.Drawing.Size(InputSize, InputSize));
                CvInvoke.CvtColor(resized, resized, ColorConversion.Bgr2Rgb);

                var inputTensor = new DenseTensor<int>(new[] { 1, InputSize, InputSize, 3 });
                var bytes = new byte[resized.Total * resized.NumberOfChannels];
                Marshal.Copy(resized.DataPointer, bytes, 0, bytes.Length);

                for (int i = 0; i < bytes.Length; i++)
                {
                    inputTensor[0, i / (InputSize * 3), i / 3 % InputSize, i % 3] = bytes[i];
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(
                        _session.InputMetadata.Keys.First(),
                        inputTensor)
                };

                using var outputs = _session.Run(inputs);
                return ExtractAllKeypoints(outputs);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private List<PointF> ExtractAllKeypoints(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
        {
            try
            {
                var tensor = outputs.First().AsTensor<float>();
                var dimensions = tensor.Dimensions.ToArray();

                var keypoints = new List<PointF>();
                int validPoints = 0;

                // Обрабатываем 4D тензор [1, 1, 17, 3]
                if (dimensions.Length == 4 && dimensions[0] == 1 && dimensions[1] == 1 && dimensions[2] == 17 && dimensions[3] == 3)
                {
                    for (int i = 0; i < 17; i++)
                    {
                        float y = tensor[0, 0, i, 0];
                        float x = tensor[0, 0, i, 1];
                        float confidence = tensor[0, 0, i, 2];

                        if (confidence > 0.3f)
                        {
                            keypoints.Add(new PointF(x, y));
                            validPoints++;
                        }
                        else
                        {
                            keypoints.Add(PointF.Empty);
                        }
                    }
                    return keypoints;
                }
                // Обрабатываем 3D тензор [1, 17, 3]
                else if (dimensions.Length == 3 && dimensions[0] == 1 && dimensions[1] == 17 && dimensions[2] == 3)
                {
                    for (int i = 0; i < 17; i++)
                    {
                        float y = tensor[0, i, 0];
                        float x = tensor[0, i, 1];
                        float confidence = tensor[0, i, 2];

                        if (confidence > 0.3f)
                        {
                            keypoints.Add(new PointF(x, y));
                            validPoints++;
                        }
                        else
                        {
                            keypoints.Add(PointF.Empty);
                        }
                    }
                    return keypoints;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private void DrawSkeleton(Mat frame, List<PointF> keypoints)
        {
            var width = frame.Width;
            var height = frame.Height;
            var scale = new SizeF(width, height);

            // Если есть точки от модели, рисуем их
            if (keypoints != null && keypoints.Count > 0)
            {
                // Рисуем линии (скелет)
                foreach (var (startIdx, endIdx) in SkeletonConnections)
                {
                    if (startIdx < keypoints.Count && endIdx < keypoints.Count &&
                        !keypoints[startIdx].IsEmpty && !keypoints[endIdx].IsEmpty)
                    {
                        var startPoint = ScalePoint(keypoints[startIdx], scale);
                        var endPoint = ScalePoint(keypoints[endIdx], scale);

                        CvInvoke.Line(
                            frame,
                            startPoint,
                            endPoint,
                            SkeletonColor, // Белый цвет для линий
                            3); // Более толстые линии
                    }
                }

                // Рисуем точки
                for (int i = 0; i < keypoints.Count; i++)
                {
                    if (!keypoints[i].IsEmpty)
                    {
                        var point = ScalePoint(keypoints[i], scale);

                        // Рисуем точку
                        CvInvoke.Circle(frame, point, 8, PointColor, -1); // Желтые точки
                    }
                }
            }
        }

        private System.Drawing.Point ScalePoint(PointF point, SizeF scale)
        {
            return new System.Drawing.Point(
                (int)(point.X * scale.Width),
                (int)(point.Y * scale.Height)
            );
        }

        private void UpdateCameraImage(Mat frame)
        {
            try
            {
                _cameraBitmap.Lock();
                var rect = new Int32Rect(0, 0, frame.Width, frame.Height);
                _cameraBitmap.WritePixels(
                    rect,
                    frame.DataPointer,
                    frame.Step * frame.Height,
                    frame.Step
                );
                _cameraBitmap.Unlock();

                OnImageUpdated?.Invoke(_cameraBitmap);
            }
            catch
            {
                // Игнорируем ошибки обновления изображения
            }
        }


        private void DrawMainMenu(Mat frame, List<System.Drawing.PointF> keypoints)
        {
            // Заливаем фиолетовым цветом
            frame.SetTo(PurpleBackground);

            // Рисуем игровые зоны
            DrawGameZones(frame);

            if (keypoints != null)
            {
                DrawSkeleton(frame, keypoints);
                CheckHandInGameZone(keypoints);

                // Рисуем анимацию загрузки
                DrawLoadingAnimation(frame, keypoints);
            }
        }

        private void DrawGameZones(Mat frame)
        {
            foreach (var zone in _gameZones)
            {
                // Рисуем квадрат игровой зоны
                CvInvoke.Rectangle(frame,
                    new Rectangle((int)zone.Area.X, (int)zone.Area.Y, (int)zone.Area.Width, (int)zone.Area.Height),
                    zone.Color,
                    3);

                // Создаем изображение с текстом
                using (Mat textImage = CreateTextImage(zone.GameName,
                                                     new MCvScalar(255, 255, 255), // Белый текст
                                                     zone.Color, // Цвет фона как у зоны
                                                     120, 30))
                {
                    // Определяем позицию для текста (центрируем по горизонтали)
                    int textX = (int)(zone.Area.X + (zone.Area.Width - textImage.Width) / 2);
                    int textY = (int)(zone.Area.Y + 15);

                    // Накладываем текст на кадр
                    if (textX + textImage.Width <= frame.Width && textY + textImage.Height <= frame.Height)
                    {
                        Mat roi = new Mat(frame, new Rectangle(textX, textY, textImage.Width, textImage.Height));
                        textImage.CopyTo(roi);
                    }
                }

                // Отображаем прогресс загрузки
                if (zone.HoverProgress > 0)
                {
                    using (Mat progressImage = CreateTextImage($"{(int)(zone.HoverProgress * 100)}%",
                                                              new MCvScalar(0, 0, 255), // Красный текст
                                                              new MCvScalar(0, 0, 0), // Черный фон
                                                              40, 20))
                    {
                        // Определяем позицию для текста прогресса (центрируем по горизонтали)
                        int progressX = (int)(zone.Area.X + (zone.Area.Width - progressImage.Width) / 2);
                        int progressY = (int)(zone.Area.Y + 50);

                        // Накладываем текст прогресса на кадр
                        if (progressX + progressImage.Width <= frame.Width && progressY + progressImage.Height <= frame.Height)
                        {
                            Mat roi = new Mat(frame, new Rectangle(progressX, progressY, progressImage.Width, progressImage.Height));
                            progressImage.CopyTo(roi);
                        }
                    }
                }
            }
        }

        private void CheckHandInGameZone(List<System.Drawing.PointF> keypoints)
        {
            if (keypoints == null || keypoints.Count < 17) return;

            // Получаем координаты рук (индексы 9 и 10)
            var leftHand = keypoints[9];
            var rightHand = keypoints[10];

            // Сбрасываем состояние наведения для всех зон
            foreach (var zone in _gameZones)
            {
                zone.IsHovered = false;
            }

            // Проверяем, находится ли какая-либо рука в игровой зоне
            foreach (var zone in _gameZones)
            {
                if (!leftHand.IsEmpty && IsPointInZone(leftHand, zone) ||
                    !rightHand.IsEmpty && IsPointInZone(rightHand, zone))
                {
                    zone.IsHovered = true;
                    break;
                }
            }
        }

        private void DrawLoadingAnimation(Mat frame, List<System.Drawing.PointF> keypoints)
        {
            if (keypoints == null || keypoints.Count < 17) return;

            // Получаем координаты рук (индексы 9 и 10)
            var leftHand = keypoints[9];
            var rightHand = keypoints[10];

            // Рисуем анимацию загрузки для каждой руки, находящейся над зоной
            foreach (var zone in _gameZones)
            {
                if (zone.IsHovered && zone.HoverProgress > 0)
                {
                    // Определяем, какая рука находится над зоной
                    System.Drawing.PointF handPoint = System.Drawing.PointF.Empty;

                    if (!leftHand.IsEmpty && IsPointInZone(leftHand, zone))
                    {
                        handPoint = leftHand;
                    }
                    else if (!rightHand.IsEmpty && IsPointInZone(rightHand, zone))
                    {
                        handPoint = rightHand;
                    }

                    if (!handPoint.IsEmpty)
                    {
                        // Преобразуем нормализованные координаты в пиксельные
                        var pixelPoint = new System.Drawing.Point(
                            (int)(handPoint.X * _screenWidth),
                            (int)(handPoint.Y * _screenHeight)
                        );

                        // Рисуем круговую анимацию загрузки
                        DrawProgressCircle(frame, pixelPoint, zone.HoverProgress);
                    }
                }
            }
        }

        public void Dispose()
{
    _isTracking = false;
    _hoverTimer?.Stop();
    _hoverTimer?.Dispose();
    _camera?.Dispose();
    _session?.Dispose();
    
    // Отписываемся от событий
    if (_boxingGame != null)
        _boxingGame.OnReturnToMainMenu -= ReturnToMainMenu;
    if (_dodgeGame != null)
        _dodgeGame.OnReturnToMainMenu -= ReturnToMainMenu;
    if (_appearanceGame != null)
        _appearanceGame.OnReturnToMainMenu -= ReturnToMainMenu;
    
    GC.SuppressFinalize(this);
}
    }

    public enum AppState
    {
        MainMenu,
        BoxingGame,
        DodgeGame,
        AppearanceGame
    }

    public class GameZone
    {
        public RectangleF Area { get; set; }
        public string GameName { get; set; }
        public bool IsHovered { get; set; }
        public MCvScalar Color { get; set; }
        public float HoverProgress { get; set; }
        public DateTime? HoverStartTime { get; set; }
        public bool IsLoading { get; set; }
    }
}