using NAudio.Wave;

public class MusicPlayer
{
    private IWavePlayer _wavePlayer;
    private AudioFileReader _audioFileReader;

    public void PlayMusic(string filePath)
    {
        if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing)
        {
            StopMusic();  // Если музыка уже играет, останавливаем её перед запуском новой
        }

        _wavePlayer = new WaveOutEvent();
        _audioFileReader = new AudioFileReader(filePath);

        _wavePlayer.Init(_audioFileReader);
        _wavePlayer.PlaybackStopped += (sender, e) =>
        {
            _audioFileReader.Dispose();
            _wavePlayer.Dispose();
        };
        _wavePlayer.Play();
    }

    public void StopMusic()
    {
        _wavePlayer?.Stop();
        _audioFileReader?.Dispose();
        _wavePlayer?.Dispose();
    }
}