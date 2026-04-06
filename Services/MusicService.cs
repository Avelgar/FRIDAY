using Friday;
using Friday.Services;
using NAudio.Wave;
using System;
using System.IO;

public class MusicService : Service
{
    private IWavePlayer _wavePlayer;
    private AudioFileReader _audioFileReader;
    private string[] _musicFiles;
    private int _currentTrackIndex;
    private string _musicFolderPath;

    public MusicService()
    {
        _currentTrackIndex = 0;
    }

    public override void Init()
    {
        LoadMusicFiles();
        base.Init();
    }

    public override void UpdateVariables()
    {
        LoadMusicFiles();
        base.UpdateVariables();
    }

    // Вынесли загрузку файлов в отдельный метод
    public void LoadMusicFiles()
    {
        _musicFolderPath = SettingManager.Setting?.MusicFolderPath;

        if (string.IsNullOrEmpty(_musicFolderPath) || !Directory.Exists(_musicFolderPath))
        {
            _musicFiles = new string[0]; // Чтобы не было null
            return;
        }

        _musicFiles = Directory.GetFiles(_musicFolderPath, "*.mp3");
        _currentTrackIndex = 0; // Сбрасываем трек при смене папки
    }

    public void Play()
    {
        if (_musicFiles == null || _musicFiles.Length == 0)
        {
            throw new InvalidOperationException("В указанной папке нет mp3 файлов.");
        }

        Stop();
        PlayMusic(_musicFiles[_currentTrackIndex]);
    }

    public void PlayMusic(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Файл не найден: " + filePath);
        }

        _wavePlayer?.Dispose();
        _audioFileReader?.Dispose();

        _wavePlayer = new WaveOutEvent();
        _audioFileReader = new AudioFileReader(filePath);

        // Устанавливаем громкость из настроек (от 0.0 до 1.0)
        _audioFileReader.Volume = SettingManager.Setting.Volume / 10f;

        _wavePlayer.Init(_audioFileReader);
        _wavePlayer.PlaybackStopped += OnPlaybackStopped;
        _wavePlayer.Play();
    }

    private void OnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        _audioFileReader?.Dispose();
        _wavePlayer?.Dispose();

        if (e.Exception == null)
        {
            NextTrack();
        }
    }

    public void Stop()
    {
        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        if (_audioFileReader != null)
        {
            _audioFileReader.Dispose();
            _audioFileReader = null;
        }
    }

    public void Pause()
    {
        _wavePlayer?.Pause();
    }

    public void Resume()
    {
        _wavePlayer?.Play();
    }

    public bool IsPlaying()
    {
        return _wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing;
    }

    public void NextTrack()
    {
        if (_musicFiles == null || _musicFiles.Length == 0) return;

        _currentTrackIndex = (_currentTrackIndex + 1) % _musicFiles.Length;
        Play();
    }

    public void PreviousTrack()
    {
        if (_musicFiles == null || _musicFiles.Length == 0) return;

        _currentTrackIndex = (_currentTrackIndex - 1 + _musicFiles.Length) % _musicFiles.Length;
        Play();
    }
}