using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Threading;
using Scarlet.DirectN.Threading;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using WebView2.Utilities;
using System.Text.Json;
using Avalonia.BlazorApp.Services;

namespace Avalonia.BlazorApp;

// Opt-in integration check against a real Avalonia window, WebView2, Razor renderer and JavaScript.
internal static class SmokeTest
{
    public static async Task RunAsync(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var report = new List<string>();
        var exitCode = 0;
        try
        {
            await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.body.dataset.ready === 'true'") == "true");
            Require(SynchronizationContext.Current is AvaloniaSynchronizationContext, "Avalonia context installed");
            Require(DirectNMessageQueue.Current is null, "No DirectN queue created by the Avalonia threading integration");
            Require(!window.Counter.IsIntegrationPageActive, "Native demo controls are hidden on Home");
            await NavigateAsync(window, "avalonia", "Blazor inside Avalonia");
            Require(window.Counter.IsIntegrationPageActive, "Native demo controls are shown on the Avalonia page");
            Require(await ScriptAsync(window, "document.getElementById('context').textContent") == "\"AvaloniaSynchronizationContext\"",
                "Razor uses Avalonia context");
            report.Add("PASS: WebView2 and Razor initialized on Avalonia's context without a DirectN queue.");

            await window.IncrementFromAvaloniaAsync();
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.getElementById('blazor-count').textContent") == "\"1\"");
            await ScriptAsync(window, "document.getElementById('increment').click()");
            await WaitUntilAsync(() => Task.FromResult(window.Counter.Count == 2));
            await window.RunBackgroundUpdateAsync();
            Require(window.Counter.Count == 3, "Worker update reached both UIs");
            report.Add("PASS: Avalonia -> Blazor, Blazor -> Avalonia and background async dispatch.");

            var dispatcher = window.BlazorHost.View.Dispatcher;
            await Task.Run(async () =>
            {
                await dispatcher.InvokeAsync((Action)dispatcher.AssertAccess);
                Require(await dispatcher.InvokeAsync((Func<int>)(() => 42)) == 42, "Synchronous result");
                await dispatcher.InvokeAsync((Func<Task>)(async () =>
                {
                    await Task.Delay(20);
                    dispatcher.AssertAccess();
                    Require(SynchronizationContext.Current is AvaloniaSynchronizationContext, "Async context");
                }));
                Require(await dispatcher.InvokeAsync((Func<Task<int>>)(async () =>
                {
                    await Task.Delay(20);
                    dispatcher.AssertAccess();
                    await dispatcher.InvokeAsync((Action)dispatcher.AssertAccess);
                    return 43;
                })) == 43, "Asynchronous result");
            });
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expected = new InvalidOperationException("Expected dispatcher test failure");
            void CaptureException(object? sender, BlazorWebViewUnhandledExceptionEventArgs e)
            {
                Dispatcher.UIThread.VerifyAccess();
                if (ReferenceEquals(e.Exception, expected))
                    reported.TrySetResult(e.Exception);
            }
            window.BlazorHost.View.UnhandledException += CaptureException;
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        await dispatcher.InvokeAsync((Func<Task>)(async () =>
                        {
                            await Task.Delay(20);
                            throw expected;
                        }));
                        throw new InvalidOperationException("Dispatched exception was lost.");
                    }
                    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expected)) { }
                });
                Require(ReferenceEquals(await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)), expected), "UI exception reporting");
            }
            finally
            {
                window.BlazorHost.View.UnhandledException -= CaptureException;
            }
            report.Add("PASS: All four dispatcher overloads, inline dispatch, async results and BlazorWebView.UnhandledException.");

            using (var integration = new AvaloniaBlazorWebViewThreading(Dispatcher.UIThread))
            {
                var hostFailure = new InvalidOperationException("Expected Avalonia unhandled exception");
                var hostReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void CaptureHostException(object? sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    if (ReferenceEquals(e.Exception, hostFailure))
                    {
                        e.Handled = true;
                        hostReported.TrySetResult();
                    }
                }
                Dispatcher.UIThread.UnhandledException += CaptureHostException;
                try
                {
                    try
                    {
                        await integration.Dispatcher.InvokeAsync((Action)(() => throw hostFailure));
                    }
                    catch (InvalidOperationException ex) when (ReferenceEquals(ex, hostFailure)) { }
                    await hostReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    Dispatcher.UIThread.UnhandledException -= CaptureHostException;
                }
            }
            report.Add("PASS: Unhandled threading failures follow Avalonia's dispatcher exception policy.");

            await NavigateAsync(window, "counter", "Counter");
            Require(!window.Counter.IsIntegrationPageActive, "Native demo controls hide when leaving the Avalonia page");
            await ClickButtonAsync(window, "Click me");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('[role=status]').textContent") == "\"Current count: 1\"");

            await NavigateAsync(window, "interop", "JS interop");
            await ClickButtonAsync(window, "Read browser info");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('.info')?.textContent.includes('User agent') === true") == "true");
            await ClickButtonAsync(window, "Ask JS to call .NET");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('[role=status]')?.textContent.includes('Called back from JavaScript') === true") == "true");

            await NavigateAsync(window, "links", "Links");
            await ScriptAsync(window, "document.querySelector('.links a[href=counter]').click()");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('h1')?.textContent") == "\"Counter\"");

            await NavigateAsync(window, "errors", "Errors");
            await ClickButtonAsync(window, "Throw in JavaScript");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('[role=status]')?.textContent.includes('Caught JS error:') === true") == "true");
            await ClickButtonAsync(window, "Throw in an event handler");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('.error-boundary h2')?.textContent") == "\"Something went wrong\"");
            await ClickButtonAsync(window, "Recover");
            await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('h1')?.textContent") == "\"Errors\"");
            report.Add("PASS: Sidebar navigation, counter, JS interop, internal links and error recovery.");

            await NavigateAsync(window, "background", "Background work");
            await ClickButtonAsync(window, "Open window (throws, DispatchExceptionAsync)");
            await ClickButtonAsync(window, "Open window (no exception)");
            await WaitUntilAsync(() => Task.FromResult(window.BackgroundWindows.Log.Count(entry => entry.Contains("background work started")) == 2));
            var backgroundWindows = window.BackgroundWindows.OpenWindows.ToArray();
            Require(backgroundWindows.Length == 2, "Background work opens Avalonia windows");
            foreach (var child in backgroundWindows)
                child.Close();
            await Task.WhenAll(backgroundWindows.Select(child => child.ClosedTask)).WaitAsync(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(() => Task.FromResult(window.BackgroundWindows.Log.Count(entry => entry.Contains("background work finished")) == 2));
            Require(!window.BackgroundWindows.Log.Any(entry => entry.Contains("UNHANDLED")), "No background exception escapes after window close");
            foreach (var id in new[] { 1, 2 })
            {
                var log = window.BackgroundWindows.Log.ToList();
                var closed = log.FindIndex(entry => entry.Contains($"#{id}: window closed"));
                var finished = log.FindIndex(entry => entry.Contains($"#{id}: background work finished"));
                Require(closed >= 0 && finished > closed, $"Window #{id} closed before its background work finished");
            }
            Require(DirectNMessageQueue.Current is null, "Background windows also use Avalonia's threading integration");
            report.Add("PASS: Both background-work variants finish after their Avalonia windows close.");

            await NavigateAsync(window, "", "Blazor in Avalonia");
            await NavigateAsync(window, "avalonia", "Blazor inside Avalonia");
            Require(window.Counter.Count == 3, "Shared state survives page navigation");
            Require(!window.Counter.ComponentDisposed, "Page navigation does not dispose the root component");

            await VerifyResizeAsync(window);
            report.Add("PASS: Native child and WebView2 grow and shrink with the Avalonia layout.");

            window.Close();
            await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
            Require(window.Counter.ComponentDisposed, "Asynchronous component disposal completed before window destruction");
            report.Add("PASS: Awaited asynchronous Razor disposal before native window close.");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            report.Add($"FAIL: {ex}");
            foreach (var child in window.BackgroundWindows.OpenWindows.ToArray())
                child.Close();
            window.Close();
            try { await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (Exception closeError) { report.Add($"Cleanup: {closeError}"); }
        }
        finally
        {
            var args = desktop.Args ?? [];
            var outputIndex = Array.IndexOf(args, "--smoke-test-output");
            var output = outputIndex >= 0 && outputIndex + 1 < args.Length
                ? args[outputIndex + 1]
                : Path.Combine(AppContext.BaseDirectory, "avalonia-smoke-test.log");
            try { await File.WriteAllLinesAsync(output, report); }
            finally { desktop.Shutdown(exitCode); }
        }
    }

    private static Task<string?> ScriptAsync(MainWindow window, string script) => window.BlazorHost.View.WebView!.ExecuteScriptAsync(script);

    private static async Task NavigateAsync(MainWindow window, string route, string heading)
    {
        await ScriptAsync(window, $"document.querySelector('nav a[href=\"{route}\"]').click()");
        await WaitUntilAsync(async () => await ScriptAsync(window, "document.querySelector('h1')?.textContent") == JsonSerializer.Serialize(heading, AppJsonContext.Default.String));
    }

    private static async Task ClickButtonAsync(MainWindow window, string text) =>
        await ScriptAsync(window, $"[...document.querySelectorAll('button')].find(b => b.textContent.trim() === {JsonSerializer.Serialize(text, AppJsonContext.Default.String)})?.click()");

    private static async Task VerifyResizeAsync(MainWindow window)
    {
        window.WindowState = WindowState.Normal;
        // A rendered route heading does not mean Avalonia has finished showing the native demo pane
        // or moving/resizing its child HWND. Establish a settled baseline before testing size changes.
        await WaitForWebViewSizeAsync(window, "initial layout after navigation");
        var originalSize = window.ClientSize;
        var originalRect = window.BlazorHost.NativeClientRect;
        var originalMinWidth = window.MinWidth;
        var originalMinHeight = window.MinHeight;
        var smallerSize = new Size(originalSize.Width - 120, originalSize.Height - 80);
        try
        {
            // Shrink and restore within the size the desktop already accepted. Growing beyond the startup
            // size can be clamped on a small CI desktop. Temporarily allow shrinking below the example's
            // preferred minimum if the initial window was already constrained to that minimum.
            window.MinWidth = Math.Min(originalMinWidth, smallerSize.Width);
            window.MinHeight = Math.Min(originalMinHeight, smallerSize.Height);
            window.Width = smallerSize.Width;
            window.Height = smallerSize.Height;
            await WaitForWebViewSizeAsync(window, "smaller WebView size", smallerSize);
            var smallerRect = window.BlazorHost.NativeClientRect;
            Require(smallerRect.right < originalRect.right && smallerRect.bottom < originalRect.bottom,
                "The native WebView must shrink in both dimensions.");

            window.Width = originalSize.Width;
            window.Height = originalSize.Height;
            await WaitForWebViewSizeAsync(window, "restored WebView size", originalSize);
            var restoredRect = window.BlazorHost.NativeClientRect;
            Require(restoredRect.right > smallerRect.right && restoredRect.bottom > smallerRect.bottom,
                "The native WebView must grow in both dimensions when restored.");
            Require(Math.Abs(restoredRect.right - originalRect.right) <= 1 && Math.Abs(restoredRect.bottom - originalRect.bottom) <= 1,
                "The restored native WebView must match its initial size.");
        }
        finally
        {
            window.Width = originalSize.Width;
            window.Height = originalSize.Height;
            window.MinWidth = originalMinWidth;
            window.MinHeight = originalMinHeight;
        }

        window.Position = new PixelPoint(window.Position.X + 20, window.Position.Y + 20);
        await WaitForWebViewSizeAsync(window, "WebView layout after moving the window");
    }

    private static async Task WaitForWebViewSizeAsync(MainWindow window, string description, Size? expectedClientSize = null)
    {
        await WaitUntilAsync(() =>
        {
            var host = window.BlazorHost;
            var native = host.NativeClientRect;
            var controller = host.View.Bounds;
            var scale = window.RenderScaling;
            return Task.FromResult(window.IsMeasureValid && window.IsArrangeValid && host.IsMeasureValid && host.IsArrangeValid
                && (expectedClientSize is not { } expected ||
                    Math.Abs(window.ClientSize.Width - expected.Width) <= 1 && Math.Abs(window.ClientSize.Height - expected.Height) <= 1)
                && native.right > 0 && native.bottom > 0
                && Math.Abs(native.right - host.Bounds.Width * scale) <= 1
                && Math.Abs(native.bottom - host.Bounds.Height * scale) <= 1
                && native.right == controller.right && native.bottom == controller.bottom);
        }, description, () => DescribeWindow(window, $"target={expectedClientSize}"));

        await WaitUntilAsync(async () =>
        {
            var native = window.BlazorHost.NativeClientRect;
            return await ScriptAsync(window,
                $"Math.abs(Math.round(innerWidth * devicePixelRatio) - {native.right}) <= 2 && " +
                $"Math.abs(Math.round(innerHeight * devicePixelRatio) - {native.bottom}) <= 2") == "true";
        }, $"browser viewport for {description}", () => DescribeWindow(window, $"target={expectedClientSize}"));
    }

    private static string DescribeWindow(MainWindow window, string baseline)
    {
        var native = window.BlazorHost.NativeClientRect;
        var controller = window.BlazorHost.View.Bounds;
        return $"{baseline}; client={window.ClientSize}; layout={window.BlazorHost.Bounds}; " +
            $"native={native.right}x{native.bottom}; controller={controller.right}x{controller.bottom}; " +
            $"scale={window.RenderScaling}; state={window.WindowState}; limits={window.MaxWidth}x{window.MaxHeight}";
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description = "smoke-test condition", Func<string>? diagnostics = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!await condition().WaitAsync(timeout.Token))
            {
                await Task.Delay(50, timeout.Token);
            }
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for {description}. {diagnostics?.Invoke()}", ex);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
