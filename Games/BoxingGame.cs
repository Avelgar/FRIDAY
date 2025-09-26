using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Drawing;
using Friday.Games;

namespace Friday.Games
{
    public class BoxingGame : GameBase
    {
        private int _bestScore;
        private float _hoverProgress;
        private DateTime? _hoverStartTime;

        public BoxingGame(int screenWidth, int screenHeight) : base(screenWidth, screenHeight)
        {
            _bestScore = 0;
            CreateInterface();
        }

        private void CreateInterface()
        {
            // Кнопка "Назад"
            _buttons.Add(new GameZone
            {
                Area = new RectangleF(_screenWidth * 0.05f, _screenHeight * 0.05f, 100, 50),
                GameName = "Назад"
            });

            // Кнопка "Начать бой"
            _buttons.Add(new GameZone
            {
                Area = new RectangleF(_screenWidth * 0.4f, _screenHeight * 0.4f, 200, 80),
                GameName = "Начать бой"
            });
        }

        public override void DrawInterface(Mat frame)
        {
            // Рисуем фон интерфейса
            CvInvoke.Rectangle(frame,
                new Rectangle(0, 0, _screenWidth, _screenHeight),
                new MCvScalar(30, 30, 60), // Темно-синий фон
                -1);

            // Рисуем кнопки
            foreach (var button in _buttons)
            {
                var buttonColor = button.IsHovered ?
                    new MCvScalar(200, 50, 50) : // Красный при наведении
                    new MCvScalar(100, 30, 30);  // Темно-красный

                CvInvoke.Rectangle(frame,
                    new Rectangle((int)button.Area.X, (int)button.Area.Y, (int)button.Area.Width, (int)button.Area.Height),
                    buttonColor,
                    -1);

                // Добавляем текст на кнопки
                using (Mat textImage = DrawingUtils.CreateTextImage(button.GameName,
                                                  new MCvScalar(255, 255, 255),
                                                  buttonColor,
                                                  (int)button.Area.Width - 20,
                                                  (int)button.Area.Height - 20))
                {
                    int textX = (int)(button.Area.X + (button.Area.Width - textImage.Width) / 2);
                    int textY = (int)(button.Area.Y + (button.Area.Height - textImage.Height) / 2);

                    if (textX + textImage.Width <= frame.Width && textY + textImage.Height <= frame.Height)
                    {
                        Mat roi = new Mat(frame, new Rectangle(textX, textY, textImage.Width, textImage.Height));
                        textImage.CopyTo(roi);
                    }
                }
            }

            using (Mat scoreImage = DrawingUtils.CreateTextImage($"Рекорд: {_bestScore} ударов", 
                                                  new MCvScalar(255, 255, 255),
                                                  new MCvScalar(200, 50, 50),
                                                  250, 40))
            {
                int textX = _screenWidth / 2 - 125;
                int textY = (int)(_screenHeight * 0.2f);

                if (textX + scoreImage.Width <= frame.Width && textY + scoreImage.Height <= frame.Height)
                {
                    Mat roi = new Mat(frame, new Rectangle(textX, textY, scoreImage.Width, scoreImage.Height));
                    scoreImage.CopyTo(roi);
                }
            }

            // Рисуем анимацию загрузки если нужно
            if (_hoverProgress > 0)
            {
                // Находим наведенную кнопку
                var hoveredButton = _buttons.FirstOrDefault(b => b.IsHovered);
                if (hoveredButton != null)
                {
                    var center = new System.Drawing.Point(
                        (int)(hoveredButton.Area.X + hoveredButton.Area.Width / 2),
                        (int)(hoveredButton.Area.Y + hoveredButton.Area.Height / 2)
                    );

                    DrawingUtils.DrawProgressCircle(frame, center, _hoverProgress);
                }
            }
        }

        public override void CheckHandInteraction(List<System.Drawing.PointF> keypoints)
        {
            if (keypoints == null || keypoints.Count < 17) return;

            // Получаем координаты рук
            var leftHand = keypoints[9];
            var rightHand = keypoints[10];

            // Сбрасываем состояние наведения для всех кнопок
            foreach (var button in _buttons)
            {
                button.IsHovered = false;
            }

            // Проверяем, находится ли какая-либо рука над кнопками
            foreach (var button in _buttons)
            {
                if (!leftHand.IsEmpty && IsPointInZone(leftHand, button) ||
                    !rightHand.IsEmpty && IsPointInZone(rightHand, button))
                {
                    button.IsHovered = true;
                    break;
                }
            }
        }

        public override void ProcessHover(float deltaTime)
        {
            // Находим наведенную кнопку
            var hoveredButton = _buttons.FirstOrDefault(b => b.IsHovered);

            if (hoveredButton != null)
            {
                if (!_hoverStartTime.HasValue)
                {
                    _hoverStartTime = DateTime.Now;
                }

                // Увеличиваем прогресс (2 секунды для полной загрузки)
                _hoverProgress = Math.Min(1.0f, _hoverProgress + deltaTime / 2.0f);

                // Если прогресс достиг 100%, выполняем действие
                if (_hoverProgress >= 1.0f)
                {
                    if (hoveredButton.GameName == "Назад")
                    {
                        // Возвращаемся в главное меню через событие
                        ReturnToMainMenu();
                    }
                    else if (hoveredButton.GameName == "Начать бой")
                    {
                        // Запускаем игру
                        StartBoxingGame();
                    }

                    // Сбрасываем прогресс
                    _hoverProgress = 0;
                    _hoverStartTime = null;
                }
            }
            else
            {
                // Если рука убрана, сбрасываем прогресс
                _hoverProgress = 0;
                _hoverStartTime = null;
            }
        }

        private void StartBoxingGame()
        {
            // Здесь будет логика запуска игры в бокс
            // Пока просто увеличим счет для демонстрации
            _bestScore += new Random().Next(5, 15);
        }

        // Методы CreateTextImage и DrawProgressCircle нужно будет добавить или сделать общими
    }
}
