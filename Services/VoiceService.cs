using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;
using Newtonsoft.Json;
using System.Net.Http;
using System.IO;
using System.Threading;
using System.Windows.Input;
using System.Windows.Forms;

namespace Friday
{
    public class VoiceService
    {
        private string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\model"));
        private readonly VoskRecognizer _recognizer;
        private readonly RenameService _renameService; // Добавляем поле для RenameService
        private WaveInEvent _waveIn;
        private static MusicService musicService = new MusicService();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string apiToken = "c83ecc77-ec9d-4055-b2ae-a16b09991421";
        private static readonly string synthesisUrl = $"https://public.api.voice.steos.io/api/v1/synthesize-controller/synthesis-by-text?authToken={apiToken}";
        public ListeningState ListeningState { get; private set; }

        public event Action<string> OnMessageReceived;
        public VoiceService(RenameService renameService)
        {
            _renameService = renameService;
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);

            ListeningState = new ListeningState();
        }
        public async Task StartListening()
        {
            try
            {
                _waveIn = new WaveInEvent();
                _waveIn.WaveFormat = new WaveFormat(16000, 1);

                if (WaveIn.DeviceCount == 0)
                {
                    OnMessageReceived?.Invoke("Нет доступных устройств для записи.");
                    return;
                }

                _waveIn.DeviceNumber = 0;
                string lastRecognizedText = string.Empty;
                bool isListeningForCommands = false;

                _waveIn.DataAvailable += async (sender, e) =>
                {
                    try
                    {
                        if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                        {
                            var result = _recognizer.Result();
                            var response = JsonConvert.DeserializeObject<RecognitionResponse>(result);
                            var recognizedText = response?.Alternatives.FirstOrDefault()?.Text;

                            if (recognizedText == _renameService.BotName)
                            {
                                OnMessageReceived?.Invoke($"Распознано: {recognizedText}");
                                await SpeakAsync("Слушаю ваши указания");
                                ListeningState.StartListening();
                                isListeningForCommands = true;
                                lastRecognizedText = string.Empty;
                            }
                            else if (isListeningForCommands && !string.IsNullOrEmpty(recognizedText) && recognizedText != lastRecognizedText)
                            {
                                var commandResult = ProcessCommand(recognizedText);

                                if (commandResult != null)
                                {
                                    await SpeakAsync(commandResult);
                                    isListeningForCommands = false;
                                }
                                lastRecognizedText = recognizedText;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnMessageReceived?.Invoke($"Ошибка при обработке аудиоданных: {ex.Message}");
                    }
                };

                _waveIn.StartRecording();
                OnMessageReceived?.Invoke("Началась запись.");
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при запуске записи: {ex.Message}");
            }
        }

        public void StopListening()
        {
            try
            {
                if (_waveIn != null)
                {
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                    _waveIn = null;
                    OnMessageReceived?.Invoke("Запись остановлена.");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка при остановке записи: {ex.Message}");
            }
        }

        private string ProcessCommand(string command)
        {
            OnMessageReceived?.Invoke($"Распознано: {command}");

            string responseMessage = string.Empty;

            // Добавьте дополнительные условия для обработки команд
            if (command.IndexOf("время", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Текущее время: {DateTime.Now.ToShortTimeString()}";
            }
            else if (command.IndexOf("имя", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = $"Меня зовут {_renameService.BotName}";
            }
            else if (command.IndexOf("погода", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WeatherService weatherService = new WeatherService();
                int dayOffset = 0; // По умолчанию прогноз на сегодня
                if (command.IndexOf("завтра", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dayOffset = 1;
                }
                else if (command.IndexOf("послезавтра", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dayOffset = 2;
                }

                responseMessage = weatherService.GetWeatherForecast(dayOffset);
            }
            else if (command.IndexOf("браво", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Спасибо! Я рад, что вам нравится!";
            }
            else if (command.IndexOf("смена имени", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int index = command.IndexOf("смена имени", StringComparison.OrdinalIgnoreCase);
                string newName = command.Substring(index + "смена имени".Length).Trim();

                if (string.IsNullOrEmpty(newName))
                {
                    responseMessage = "Пожалуйста, укажите новое имя после команды 'смена имени'";
                }
                else
                {
                    _renameService.BotName = newName;
                    responseMessage = $"Имя успешно изменено на {newName}";
                }
            }
            else if (command.IndexOf("включить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Включаю музыку...";
                musicService.Play();
            }
            else if (command.IndexOf("выключить музыку", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Выключаю музыку...";
                Thread.Sleep(1500);
                musicService.Stop();
            }
            else if (command.IndexOf("следующий трек", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                responseMessage = "Переключаю...";
                musicService.NextTrack();
            }
            else
            {
                CommandManager commandManager = new CommandManager();
                var customCommand = commandManager.FindCommandByTrigger(command);
                if (customCommand != null)
                {
                    CustomCommandService.ExecuteCommand(customCommand);
                }
                else
                {
                    responseMessage = "Команда не распознана.";
                }
            }

            // Логируем ответ
            OnMessageReceived?.Invoke($"Ответ: {responseMessage}");

            return responseMessage; // Возвращаем ответ
        }


        private async Task SpeakAsync(string text)
        {
            bool wasMusicPlaying = musicService.IsPlaying();
            if (wasMusicPlaying)
            {
                musicService.Pause();
            }

            try
            {
                var response = await httpClient.PostAsync(synthesisUrl,
                    new StringContent(JsonConvert.SerializeObject(new
                    {
                        voiceId = 1,
                        text
                    }), Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    OnMessageReceived?.Invoke($"Ошибка синтеза речи: {errorContent}");
                }

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<SynthesisResponse>(responseContent);

                    byte[] audioData = Convert.FromBase64String(result.FileContents);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "output.mp3");


                    await Task.Run(() => File.WriteAllBytes(filePath, audioData));

                    using (var audioFile = new AudioFileReader(filePath))
                    using (var waveOut = new WaveOutEvent())
                    {
                        waveOut.Init(audioFile);
                        waveOut.Play();

                        while (waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            await Task.Delay(100);
                        }
                    }

                    File.Delete(filePath);
                }
                else
                {
                    OnMessageReceived?.Invoke("Ошибка синтеза речи.");
                }
            }
            catch (Exception ex)
            {
                OnMessageReceived?.Invoke($"Ошибка: {ex.Message}");
            }
            finally
            {
                if (wasMusicPlaying)
                {
                    musicService.Resume();
                }
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
