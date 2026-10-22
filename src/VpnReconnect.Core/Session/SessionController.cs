namespace VpnReconnect.Session;

public sealed class SessionController
{
    public bool CheckerRunning { get; private set; }

    public bool WindowVisible { get; private set; }

    public void StartBackground()
    {
        CheckerRunning = true;
        WindowVisible = false;
    }

    public void ShowWindow()
    {
        WindowVisible = true;
    }

    public void CloseWindow()
    {
        WindowVisible = false;
    }

    public void Exit()
    {
        CheckerRunning = false;
        WindowVisible = false;
    }
}
