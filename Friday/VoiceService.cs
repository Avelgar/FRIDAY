using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Timers;

namespace Friday
{
    public class VoiceService
    {
        private string modelPath = "C:\\Users\\Maksim\\RiderProjects\\Friday\\Friday\\model";
        private readonly VoskRecognizer _recognizer;
        private readonly string botName = "пятница"; // Задайте имя бота
        private Timer commandTimer; // Таймер для отслеживания времени
        private bool isListening; // Флаг для отслеживания состояния

        public VoiceService()
        {
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);
            commandTimer = new Timer(5000); // Устанавливаем таймер на 5 секунд
            commandTimer.Elapsed += OnCommandTimerElapsed;
            commandTimer.AutoReset = false; // Таймер не будет перезапускаться автоматически
        }

        public Task StartListening() // Убрано 'async'
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(16000, 1);
                waveIn.DataAvailable += (sender, e) =>
                {
                    if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var result = _recognizer.Result();
                        //Console.WriteLine(result);

                        // Десериализация JSON-ответа
                        var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                        var recognizedText = response?.Alternatives.FirstOrDefault()?.Text;

                        if (recognizedText == botName)
                        {
                            Console.WriteLine("Слушаю вас");
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
                Console.WriteLine("Говорите... (нажмите Enter для выхода)");
                Console.ReadLine();
                waveIn.StopRecording();
            }

            return Task.CompletedTask; // Добавлено возвращение завершенной задачи
        }


        private void OnCommandTimerElapsed(object sender, ElapsedEventArgs e)
        {
            isListening = false; // Сбрасываем флаг, когда таймер истек
            //Console.WriteLine("Время ожидания истекло. Бот больше не слушает команды.");
        }

        private void ProcessCommand(string command)
        {
            // Обработка различных команд
            if (string.IsNullOrEmpty(command)) return;

            // Пример обработки команд
            if (command.IndexOf("погода", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("Запрашиваю погоду...");
                // Логика для получения погоды
            }
            else if (command.IndexOf("время", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine($"Текущее время: {DateTime.Now}");
            }
            else if (command.IndexOf("Как тебя зовут?", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine($"Меня зовут {botName}");
            }
            else
            {
                Console.WriteLine("Команда не распознана.");
            }
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

