using Avalonia.Controls;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;

namespace Avalonia.BlazorApp;

// Shared window lifetime for the main example and the background-work windows.
internal class BlazorWindow : Window
{
    private bool _closing;
    private bool _allowClose;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BlazorViewHost BlazorHost { get; }
    public Task ClosedTask => _closed.Task;

    public BlazorWindow(IServiceProvider services, Action<BlazorWebView> configureComponents)
    {
        BlazorHost = new BlazorViewHost(services, configureComponents) { Focusable = true };
        Content = BlazorHost;
        PositionChanged += (_, _) => BlazorHost.NotifyWindowMoved();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                BlazorHost.SetWindowMinimized(WindowState == WindowState.Minimized);
        };
        Closing += OnClosing;
        Closed += (_, _) => _closed.TrySetResult();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        // Keep the native parent and message loop alive until asynchronous Razor cleanup finishes.
        e.Cancel = true;
        if (!_closing)
        {
            _closing = true;
            _ = DisposeThenCloseAsync();
        }
    }

    private async Task DisposeThenCloseAsync()
    {
        try
        {
            await BlazorHost.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
        }
        finally
        {
            _allowClose = true;
            // Close outside the current Closing event, even if disposal completed synchronously.
            Dispatcher.Post(Close);
        }
    }
}
