using System.Timers;

public class ListeningState
{
    private bool isListening;
    public bool IsListeningForPassword { get; set; }
    private System.Timers.Timer commandTimer;

    public event Action OnTimeout;

    public ListeningState()
    {
        commandTimer = new System.Timers.Timer(500000);
        commandTimer.Elapsed += OnCommandTimerElapsed;
        commandTimer.AutoReset = false;
    }

    private void OnCommandTimerElapsed(object sender, ElapsedEventArgs e)
    {
        StopListening();
        OnTimeout?.Invoke();
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
