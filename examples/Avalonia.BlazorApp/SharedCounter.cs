using Avalonia.Threading;

namespace Avalonia.BlazorApp;

// One service shared by Avalonia and Blazor through DI. All mutations happen on Avalonia's UI thread.
public sealed class SharedCounter
{
    public int Count { get; private set; }
    public string LastSource { get; private set; } = "Neither side yet";
    public bool ComponentDisposed { get; private set; }
    public event Action? Changed;

    public void Increment(string source)
    {
        Dispatcher.UIThread.VerifyAccess();
        Count++;
        LastSource = source;
        Changed?.Invoke();
    }

    public void NotifyComponentDisposed()
    {
        Dispatcher.UIThread.VerifyAccess();
        ComponentDisposed = true;
    }
}
