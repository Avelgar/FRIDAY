using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Threading;
using System.Timers;
using System.Windows;

namespace Friday
{
    public class VoiceService
    {
        private string modelPath = "C:\\Users\\nikon\\Githup\\FRIDAY\\Friday\\model\\vosk-model-small-ru-0.22";
        private readonly VoskRecognizer _recognizer;
        private readonly string botName = "пятница";
        private static MusicPlayer musicPlayer = new MusicPlayer();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiToken = "60bbe7c1-7587-4658-82a4-4ac7481016c4";
        private static readonly string synthesisUrl = $"https://public.api.voice.steos.io/api/v1/synthesize-controller/synthesis-by-text?authToken={apiToken}";
        public ListeningState ListeningState { get; private set; }


        public event Action<string> OnMessageReceived;

        public VoiceService()
        {
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);

            // Создаем объект ListeningState
            ListeningState = new ListeningState();
            ListeningState.OnTimeout += OnListeningTimeout;
        }

        private void OnListeningTimeout()
        {
            OnMessageReceived?.Invoke("Время ожидания истекло. Я перестаю слушать команды.");
            SpeakAsync("Время ожидания истекло.");
        }

        public async Task StartListening()
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(16000, 1);

                // Проверка на доступные устройства
                if (WaveIn.DeviceCount == 0)
                {
                    OnMessageReceived?.Invoke("Нет доступных устройств для записи.");
                    return;
                }

                // Присваиваем активное устройство ввода
                waveIn.DeviceNumber = 0; // Убедитесь, что индекс устройства правильный
                waveIn.DataAvailable += async (sender, e) =>
                {
                    if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        var result = _recognizer.Result();
                        var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                        var recognizedText = response?.Alternatives.FirstOrDefault()?.Text;

                        OnMessageReceived?.Invoke($"Распознано: {recognizedText}");

                        if (recognizedText == botName)
                        {
                            await SpeakAsync("Слушаю ваши указания");
                            ListeningState.StartListening(); // Запускаем прослушивание
                        }
                        else if (ListeningState.IsListening()) // Проверяем состояние
                        {
                            ProcessCommand(recognizedText); // Обрабатываем команду, если в режиме прослушивания
                        }
                    }
                };

                // Запускаем запись
                waveIn.StartRecording();

                // Проверка, если запись начала работать
                OnMessageReceived?.Invoke("Началась запись.");

                await Task.Delay(Timeout.Infinite); // Держим процесс в ожидании
            }
        }



        private void ProcessCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            string responseMessage = string.Empty;

            if (command.IndexOf("погода", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Запрашиваю погоду...";
                SpeakAsync(responseMessage).Wait();
            }
            else if (command.IndexOf("время", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Текущее время: {DateTime.Now.ToShortTimeString()}";
                SpeakAsync(responseMessage).Wait();
            }
            else if (command.IndexOf("ящер", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Слушай, брат, про ящеров и древних русов!";
                SpeakAsync(responseMessage).Wait();
            }
            else if (command.IndexOf("Имя", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Меня зовут {botName}";
                SpeakAsync(responseMessage).Wait();
            }
            else if (command.IndexOf("включить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Включаю музыку...";
                SpeakAsync(responseMessage).Wait();
                musicPlayer.PlayMusic(@"C:\Users\nikon\Downloads\vivaldi_zima_1.mp3");
            }
            else if (command.IndexOf("выключить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Выключаю музыку...";
                SpeakAsync(responseMessage).Wait();
                musicPlayer.StopMusic();
            }
            else
            {
                responseMessage = "Команда не распознана.";
                SpeakAsync(responseMessage).Wait();
            }

            OnMessageReceived?.Invoke(responseMessage);
        }

        private async Task SpeakAsync(string text)
        {
            // Запрос к API для синтеза речи
            var response = await httpClient.PostAsync(synthesisUrl,
                new StringContent(JsonConvert.SerializeObject(new
                {
                    voiceId = 100,  // Выбирайте голос по своему предпочтению
                    text,
                    format = "mp3"
                }), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SynthesisResponse>(responseContent);

                byte[] audioData = Convert.FromBase64String(result.FileContents);
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "output.mp3");

                // Асинхронная запись в файл
                await Task.Run(() => File.WriteAllBytes(filePath, audioData));

                // Воспроизводим файл с озвучкой
                musicPlayer.PlayMusic(filePath);
            }
            else
            {
                OnMessageReceived?.Invoke("Ошибка синтеза речи.");
            }
        }

        

    }



    public class RecognitionResponse
    {
        public Alternative[] Alternatives { get; set; }
    }

    public class Alternative
    {
        public string Text { get; set; }
    }

    public class SynthesisResponse
    {
        public string FileContents { get; set; }
    }
}
