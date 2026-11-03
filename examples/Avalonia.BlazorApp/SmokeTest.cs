using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Scarlet.DirectN.Threading;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using WebView2.Utilities;

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
                await dispatcher.InvokeAsync((Action)(() => dispatcher.AssertAccess()));
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
                    await dispatcher.InvokeAsync((Action)(() => dispatcher.AssertAccess()));
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

            var originalRect = window.BlazorHost.NativeClientRect;
            window.Width += 120;
            window.Height += 80;
            window.Position = new PixelPoint(window.Position.X + 20, window.Position.Y + 20);
            await WaitUntilAsync(() =>
            {
                var native = window.BlazorHost.NativeClientRect;
                var bounds = window.BlazorHost.View.Bounds;
                return Task.FromResult(native.right > originalRect.right && native.bottom > originalRect.bottom
                    && native.right == bounds.right && native.bottom == bounds.bottom);
            });
            window.Width -= 120;
            window.Height -= 80;
            await WaitUntilAsync(() =>
            {
                var native = window.BlazorHost.NativeClientRect;
                var bounds = window.BlazorHost.View.Bounds;
                return Task.FromResult(native.right == originalRect.right && native.bottom == originalRect.bottom
                    && native.right == bounds.right && native.bottom == bounds.bottom);
            });
            await WaitUntilAsync(async () =>
            {
                var renderedWidth = await ScriptAsync(window, "Math.round(innerWidth * devicePixelRatio)");
                return int.TryParse(renderedWidth, out var width) && Math.Abs(width - window.BlazorHost.NativeClientRect.right) <= 2;
            });
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

    private static Task<string?> ScriptAsync(MainWindow window, string script) =>
        window.BlazorHost.View.WebView!.ExecuteScriptAsync(script);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition().WaitAsync(timeout.Token))
            await Task.Delay(50, timeout.Token);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
