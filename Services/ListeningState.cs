using System.Timers;

public class ListeningState
{
    private bool isListening;
    private System.Timers.Timer commandTimer;

    public event Action OnTimeout;

    public ListeningState()
    {
        commandTimer = new System.Timers.Timer(500000); // Таймер на 500 секунд
        commandTimer.Elapsed += OnCommandTimerElapsed;
        commandTimer.AutoReset = false; // Таймер не будет перезапускаться автоматически
    }

    private void OnCommandTimerElapsed(object sender, ElapsedEventArgs e)
    {
        StopListening();
        OnTimeout?.Invoke(); // Сообщаем, что время ожидания истекло
    }

    public bool IsListening()
    {
        return isListening;
    }

    public void StartListening()
    {
        isListening = true;
        commandTimer.Start();
    }

    public void StopListening()
    {
        isListening = false;
        commandTimer.Stop();
    }

    public void ResetTimer()
    {
        commandTimer.Stop();
        commandTimer.Start();
    }
}
