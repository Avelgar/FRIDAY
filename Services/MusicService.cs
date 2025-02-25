using NAudio.Wave;
using System.IO;

public class MusicService
{
    private IWavePlayer _wavePlayer;
    private AudioFileReader _audioFileReader;
    private string[] _musicFiles;
    private int _currentTrackIndex;
    private readonly string _musicFolderPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\music"));

    public MusicService()
    {
        if (!Directory.Exists(_musicFolderPath))
        {
            throw new DirectoryNotFoundException("The specified folder does not exist: " + _musicFolderPath);
        }

        _musicFiles = Directory.GetFiles(_musicFolderPath, "*.mp3");
        if (_musicFiles.Length == 0)
        {
            throw new FileNotFoundException("No MP3 files found in the specified folder.");
        }

        _currentTrackIndex = 0;
    }

    public void Play()
    {
        if (_musicFiles == null || _musicFiles.Length == 0)
        {
            throw new InvalidOperationException("No music files available to play.");
        }

        Stop();

        PlayMusic(_musicFiles[_currentTrackIndex]);
    }

    public void PlayMusic(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found: " + filePath);
        }

        _wavePlayer?.Dispose();
        _audioFileReader?.Dispose();
        _wavePlayer = new WaveOutEvent();
        _audioFileReader = new AudioFileReader(filePath);

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
