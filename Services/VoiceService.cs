using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Timers;
using System.Threading;

namespace Friday
{
    public class VoiceService
    {
        // Добавляем событие для передачи сообщений
        public event Action<string> OnMessageReceived;

        private string modelPath = "C:\\Users\\nikon\\Githup\\FRIDAY\\model";
        private readonly VoskRecognizer _recognizer;
        private readonly string botName = "пятница"; // Имя бота
        private System.Timers.Timer commandTimer; // Таймер для отслеживания времени
        private bool isListening; // Флаг для отслеживания состояния
        public bool IsListening => isListening; // Свойство для доступа к состоянию прослушивания

        public VoiceService()
        {
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);
            commandTimer = new System.Timers.Timer(50000); // Таймер на 50 секунд
            commandTimer.Elapsed += OnCommandTimerElapsed;
            commandTimer.AutoReset = false; // Таймер не будет перезапускаться автоматически
        }

        public async Task StartListening() // Метод для начала прослушивания
        {
            if (isListening) return; // Если уже слушаем, не начинаем заново

            isListening = true;

            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(16000, 1);
                waveIn.DataAvailable += (sender, e) =>
                {
                    if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var result = _recognizer.Result();
                        var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                        var recognizedText = response?.Alternatives.FirstOrDefault()?.Text;

                        if (recognizedText == botName)
                        {
                            // Когда услышан бот, начинаем слушать команды
                            OnMessageReceived?.Invoke("Слушаю вас...");
                            commandTimer.Start(); // Запускаем таймер
                        }
                        else if (isListening)
                        {
                            ProcessCommand(recognizedText); // Обработка команды
                        }
                    }
                };

                waveIn.StartRecording();
                await Task.Delay(Timeout.Infinite); // Ожидание событий
            }
        }

        public void StopListening() // Метод для остановки прослушивания
        {
            isListening = false;
            OnMessageReceived?.Invoke("Прослушивание остановлено.");
        }

        private void OnCommandTimerElapsed(object sender, ElapsedEventArgs e)
        {
            isListening = false; // Таймер истек — останавливаем прослушивание
            OnMessageReceived?.Invoke("Время ожидания истекло. Бот больше не слушает.");
        }

        private void ProcessCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            string responseMessage = string.Empty;

            // Логика обработки команд
            if (command.IndexOf("погода", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Запрашиваю погоду...";
            }
            else if (command.IndexOf("время", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Текущее время: {DateTime.Now}";
            }
            else if (command.IndexOf("Как тебя зовут", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Меня зовут {botName}";
            }
            else
            {
                responseMessage = "Команда не распознана.";
            }

            // Вызываем событие с ответом
            OnMessageReceived?.Invoke(responseMessage);
        }
    }

    // Классы для десериализации JSON
    public class RecognitionResponse
    {
        public Alternative[] Alternatives { get; set; }
    }

    public class Alternative
    {
        public float Confidence { get; set; }
        public Result[] Result { get; set; }
        public string Text { get; set; }
    }

    public class Result
    {
        public float Start { get; set; }
        public float End { get; set; }
        public string Word { get; set; }
    }
}
