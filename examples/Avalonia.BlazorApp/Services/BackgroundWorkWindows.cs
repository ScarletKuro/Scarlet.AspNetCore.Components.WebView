using Avalonia.Threading;
using Avalonia.BlazorApp.Components.Background;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;

namespace Avalonia.BlazorApp.Services;

// Reproduces the same closed-window background work as the other hosts, using Avalonia windows.
public sealed class BackgroundWorkWindows(IServiceProvider services, NativeDialogs dialogs)
{
    private readonly List<string> _log = [];
    private int _nextId;

    public event Action? Changed;
    public IReadOnlyList<string> Log => _log;

    public void Open(bool throwException)
    {
        Dispatcher.UIThread.VerifyAccess();
        var id = ++_nextId;
        var window = new BlazorWindow(services, view =>
        {
            view.RootComponents.Add<BackgroundWorkWindow>("#app", new Dictionary<string, object?>
            {
                [nameof(BackgroundWorkWindow.WindowId)] = id,
                [nameof(BackgroundWorkWindow.ThrowException)] = throwException,
            });
            view.UnhandledException += (_, e) =>
            {
                Add($"#{id}: UNHANDLED {e.Exception.GetType().Name}: {e.Exception.Message}");
                dialogs.ShowError(e.Exception.ToString());
                e.Handled = true;
            };
        })
        {
            Title = $"Background work #{id} - close me while loading",
            Width = 560,
            Height = 360,
        };
        window.Closed += (_, _) => Add($"#{id}: window closed");
        Add($"#{id}: opened ({(throwException ? "throws, then DispatchExceptionAsync" : "no exception")})");
        window.Show();
    }

    public void Add(string entry)
    {
        Dispatcher.UIThread.VerifyAccess();
        _log.Add($"{DateTime.Now:HH:mm:ss}  {entry}");
        Changed?.Invoke();
    }
}
