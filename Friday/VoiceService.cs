using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;

namespace Friday
{
    public class VoiceService
    {
        private string modelPath = "C:\\Users\\Maksim\\RiderProjects\\Friday\\Friday\\model";
        private readonly VoskRecognizer _recognizer;

        public VoiceService()
        {
            Vosk.Vosk.SetLogLevel(-1);
            Model model = new Model(modelPath);
            _recognizer = new VoskRecognizer(model, 16000.0f);
            _recognizer.SetMaxAlternatives(1);
            _recognizer.SetWords(true);
        }

        public async Task StartListening()
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(16000, 1);
                waveIn.DataAvailable += (sender, e) =>
                {
                    if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        Console.WriteLine(_recognizer.Result());
                    }
                };

                waveIn.StartRecording();
                Console.WriteLine("Говорите... (нажмите Enter для выхода)");
                Console.ReadLine();
                waveIn.StopRecording();
            }
        }
    }
}