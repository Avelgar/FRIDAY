using Emgu.CV;

namespace Friday.Games
{
    public abstract class GameBase
    {
        protected int _screenWidth;
        protected int _screenHeight;
        protected List<GameZone> _buttons;

        public GameBase(int screenWidth, int screenHeight)
        {
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            _buttons = new List<GameZone>();
        }

        public abstract void DrawInterface(Mat frame);
        public abstract void CheckHandInteraction(List<System.Drawing.PointF> keypoints);
        public abstract void ProcessHover(float deltaTime);

        protected bool IsPointInZone(System.Drawing.PointF point, GameZone zone)
        {
            var pixelPoint = new System.Drawing.Point(
                (int)(point.X * _screenWidth),
                (int)(point.Y * _screenHeight)
            );

            return zone.Area.Contains(pixelPoint);
        }

        public event Action OnReturnToMainMenu;

        // Метод для вызова события
        protected void ReturnToMainMenu()
        {
            OnReturnToMainMenu?.Invoke();
        }
    }
}
