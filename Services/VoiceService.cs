using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Timers;
using System.Diagnostics;
using System.Threading;

namespace Friday
{
    public class VoiceService
    {
        // Добавьте это событие для передачи сообщений
        public event Action<string> OnMessageReceived;

        // Остальная логика вашего VoiceService...
        private string modelPath = "D:\\projects\\Friday\\model";
        private readonly VoskRecognizer _recognizer;
        private readonly string botName = "джарвис"; // Задайте имя бота
        private System.Timers.Timer commandTimer; // Таймер для отслеживания времени
        private bool isListening; // Флаг для отслеживания состояния
        public VoiceService()
        {
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);
            commandTimer = new System.Timers.Timer(5000); // Устанавливаем таймер на 5 секунд
            commandTimer.Elapsed += OnCommandTimerElapsed;
            commandTimer.AutoReset = false; // Таймер не будет перезапускаться автоматически
        }
        public async Task StartListening() // Убедитесь, что метод возвращает Task
        {

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
                            //Console.WriteLine("Слушаю вас");

                            isListening = true; // Устанавливаем флаг
                            commandTimer.Start(); // Запускаем таймер
                        }
                        else if (isListening)
                        {
                            ProcessCommand(recognizedText); // Обработка команды
                        }
                    }
                };

                waveIn.StartRecording();

                // Замените Console.ReadLine() на асинхронное ожидание
                await Task.Delay(Timeout.Infinite); // Ожидание без блокировки
            }
        }



        private void OnCommandTimerElapsed(object sender, ElapsedEventArgs e)
        {
            isListening = false; // Сбрасываем флаг, когда таймер истек
                                 //Console.WriteLine("Время ожидания истекло. Бот больше не слушает команды.");
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

            // Вывод в консоль для отладки
            Console.WriteLine($"Обработанная команда: {command}, ответ: {responseMessage}");

            // Вызываем событие
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
