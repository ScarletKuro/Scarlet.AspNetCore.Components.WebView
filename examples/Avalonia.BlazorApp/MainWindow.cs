using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.BlazorApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Avalonia.BlazorApp;

internal sealed class MainWindow : BlazorWindow
{
    private readonly TextBlock _count = new() { FontSize = 28 };
    private readonly TextBlock _source = new() { TextWrapping = Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Media.TextWrapping.Wrap };
    private readonly StackPanel _integrationPanel;

    public SharedCounter Counter { get; }
    public BackgroundWorkWindows BackgroundWindows { get; }

    public MainWindow(IServiceProvider services) : base(services)
    {
        // Reuse the base window's host inside the main window's surrounding Avalonia layout.
        Content = null;
        Title = "Avalonia + Win32Aot BlazorWebView";
        Width = 1280;
        Height = 800;
        MinWidth = 1000;
        MinHeight = 520;
        Counter = services.GetRequiredService<SharedCounter>();
        BackgroundWindows = services.GetRequiredService<BackgroundWorkWindows>();
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

        _integrationPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Width = 240,
            IsVisible = false,
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Avalonia controls", FontSize = 22 },
                new TextBlock { Text = "One UI thread, Avalonia's dispatcher, and a shared .NET service.", TextWrapping = Media.TextWrapping.Wrap },
                _count, _source, increment, background, focus,
                new TextBlock { Text = "These controls share state with the Blazor integration page. Navigate away to return to the standard example layout.", TextWrapping = Media.TextWrapping.Wrap },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(_integrationPanel);
        Grid.SetColumn(BlazorHost, 1);
        grid.Children.Add(BlazorHost);
        var statusBar = new Border { Padding = new Thickness(16, 8), Child = _status };
        Grid.SetRow(statusBar, 1);
        Grid.SetColumnSpan(statusBar, 2);
        grid.Children.Add(statusBar);
        Content = grid;

        Counter.Changed += UpdateCounter;
        UpdateCounter();
        SetStatus("Starting WebView2...");
        Activated += async (_, _) =>
        {
            try
            {
                await BlazorHost.Ready;
                await BlazorHost.View.TryDispatchAsync(sp => sp.GetRequiredService<HostEvents>().NotifyActivated());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
        };
        Closed += (_, _) => Counter.Changed -= UpdateCounter;
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
        _integrationPanel.IsVisible = Counter.IsIntegrationPageActive;
    }

    private void SetStatus(string text)
    {
        Dispatcher.VerifyAccess();
        _status.Text = text;
    }
}
