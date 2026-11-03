using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Avalonia.BlazorApp;

internal sealed class MainWindow : Window
{
    private readonly TextBlock _count = new() { FontSize = 28 };
    private readonly TextBlock _source = new() { TextWrapping = Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Media.TextWrapping.Wrap };
    private bool _closing;
    private bool _allowClose;

    public BlazorViewHost BlazorHost { get; }
    public SharedCounter Counter { get; }
    public Task ClosedTask => _closed.Task;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainWindow(IServiceProvider services)
    {
        Title = "Avalonia + Win32Aot BlazorWebView";
        Width = 1100;
        Height = 740;
        MinWidth = 760;
        MinHeight = 520;
        Counter = services.GetRequiredService<SharedCounter>();
        BlazorHost = new BlazorViewHost(services) { Focusable = true };
        BlazorHost.StatusChanged += SetStatus;

        var increment = new Button { Content = "Send +1 to Blazor", HorizontalAlignment = HorizontalAlignment.Stretch };
        increment.Click += async (_, _) =>
        {
            try { await IncrementFromAvaloniaAsync(); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
        };
        var background = new Button { Content = "Update from a worker", HorizontalAlignment = HorizontalAlignment.Stretch };
        background.Click += async (_, _) =>
        {
            try { await RunBackgroundUpdateAsync(); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
        };
        var focus = new Button { Content = "Focus Blazor", HorizontalAlignment = HorizontalAlignment.Stretch };
        focus.Click += (_, _) => BlazorHost.Focus();

        var sidebar = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Avalonia controls", FontSize = 22 },
                new TextBlock { Text = "One UI thread, Avalonia's dispatcher, and a shared .NET service.", TextWrapping = Media.TextWrapping.Wrap },
                _count, _source, increment, background, focus,
                new TextBlock { Text = "The pane on the right contains local Razor components hosted by WebView2Aot.", TextWrapping = Media.TextWrapping.Wrap },
                _status,
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
        grid.Children.Add(sidebar);
        Grid.SetColumn(BlazorHost, 1);
        grid.Children.Add(BlazorHost);
        Content = grid;

        Counter.Changed += UpdateCounter;
        UpdateCounter();
        SetStatus("Starting WebView2...");
        PositionChanged += (_, _) => BlazorHost.NotifyWindowMoved();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                BlazorHost.SetWindowMinimized(WindowState == WindowState.Minimized);
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            Counter.Changed -= UpdateCounter;
            _closed.TrySetResult();
        };
    }

    public async Task IncrementFromAvaloniaAsync()
    {
        await BlazorHost.Ready;
        var dispatched = await BlazorHost.View.TryDispatchAsync(sp =>
            sp.GetRequiredService<SharedCounter>().Increment("Avalonia button through TryDispatchAsync"));
        SetStatus(dispatched ? "Avalonia updated the Blazor scope." : "Blazor is not attached yet; try again.");
    }

    public async Task RunBackgroundUpdateAsync()
    {
        await BlazorHost.Ready;
        SetStatus("Worker running...");
        await Task.Run(async () =>
        {
            await BlazorHost.View.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(75);
                Dispatcher.VerifyAccess();
                if (SynchronizationContext.Current is not AvaloniaSynchronizationContext)
                    throw new InvalidOperationException("The continuation lost Avalonia's synchronization context.");
                Counter.Increment("Worker through Avalonia dispatcher (including after await)");
            });
        });
        SetStatus("Worker completed; callback and await continuation both used Avalonia's UI context.");
    }

    private void UpdateCounter()
    {
        Dispatcher.VerifyAccess();
        _count.Text = $"Shared count: {Counter.Count}";
        _source.Text = $"Last update: {Counter.LastSource}";
    }

    private void SetStatus(string text)
    {
        Dispatcher.VerifyAccess();
        _status.Text = text;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        // Keep the window, native parent and message loop alive until Razor/JS disposal has completed.
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
            SetStatus("Disposing Razor components before closing...");
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
