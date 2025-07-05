using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Emgu.CV.CvEnum;
using System.Linq;
using System.Collections.Generic;

namespace Friday.Services
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
        private Mat _currentFrame;

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

        // Названия ключевых точек
        private static readonly string[] KeypointNames = new[]
        {
        "nose", "Left eye", "Right eye", "Left ear", "Right ear",
        "Left shoulder", "Right shoulder", "Left elbow", "Right elbow",
        "Left hand", "Right hand", "Left thigh", "Right thigh",
        "Left knee", "Right knee", "Left ankle", "Right ankle"
        };

        public PoseTrackingService()
        {
            string modelPath = GetModelPath();
            try
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                _session = new InferenceSession(modelPath, sessionOptions);

                _camera = new VideoCapture(0);
                if (!_camera.IsOpened)
                {
                    MessageBox.Show("Camera initialization failed");
                    throw new Exception("Camera initialization failed");
                }

                _cameraBitmap = new WriteableBitmap(
                    _camera.Width,
                    _camera.Height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Init error: {ex}");
                Dispose();
                throw;
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

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var keypoints = ProcessFrame(frame);
                            if (keypoints != null)
                            {
                                DrawSkeleton(frame, keypoints);
                            }
                            UpdateCameraImage(frame);
                        });

                        Thread.Sleep(30);
                    }
                }
            });
        }

        private List<System.Drawing.PointF> ProcessFrame(Mat frame)
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
                    inputTensor[0, i / (InputSize * 3), (i / 3) % InputSize, i % 3] = bytes[i];
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor<int>(
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

        private List<System.Drawing.PointF> ExtractAllKeypoints(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
        {
            try
            {
                var tensor = outputs.First().AsTensor<float>();
                var dimensions = tensor.Dimensions.ToArray();
                //string tensorInfo = $"Tensor dimensions: {string.Join(", ", dimensions)}";
                //MessageBox.Show(tensorInfo);

                var keypoints = new List<System.Drawing.PointF>();
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
                            keypoints.Add(new System.Drawing.PointF(x, y));
                            validPoints++;
                        }
                        else
                        {
                            keypoints.Add(System.Drawing.PointF.Empty);
                        }
                    }
                    //MessageBox.Show($"Detected {validPoints} valid keypoints (4D tensor)");
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
                            keypoints.Add(new System.Drawing.PointF(x, y));
                            validPoints++;
                        }
                        else
                        {
                            keypoints.Add(System.Drawing.PointF.Empty);
                        }
                    }
                    MessageBox.Show($"Detected {validPoints} valid keypoints (3D tensor)");
                    return keypoints;
                }
                else
                {
                    MessageBox.Show($"Unexpected tensor shape: [{string.Join(",", dimensions)}]");
                    return null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ExtractAllKeypoints error: {ex.Message}");
                return null;
            }
        }

        private void DrawSkeleton(Mat frame, List<System.Drawing.PointF> keypoints)
        {
            var width = frame.Width;
            var height = frame.Height;
            var scale = new System.Drawing.SizeF(width, height);

            // Если есть точки от модели, рисуем их поверх тестовых
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
                            new MCvScalar(0, 255, 0), // Зеленый цвет
                            2);
                    }
                }

                // Рисуем точки
                for (int i = 0; i < keypoints.Count; i++)
                {
                    if (!keypoints[i].IsEmpty)
                    {
                        var point = ScalePoint(keypoints[i], scale);

                        // Рисуем точку
                        CvInvoke.Circle(frame, point, 5, new MCvScalar(0, 0, 255), -1); // Красный цвет

                        // Подписываем точку (только для основных точек)
                        if (i < KeypointNames.Length)
                        {
                            CvInvoke.PutText(
                                frame,
                                KeypointNames[i],
                                new System.Drawing.Point(point.X + 10, point.Y),
                                FontFace.HersheySimplex,
                                0.4,
                                new MCvScalar(255, 255, 255), // Белый цвет
                                1);
                        }
                    }
                }
            }
        }
        

        private System.Drawing.Point ScalePoint(System.Drawing.PointF point, System.Drawing.SizeF scale)
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

        public void Dispose()
        {
            _isTracking = false;
            _camera?.Dispose();
            _session?.Dispose();
            _currentFrame?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}