using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.BlazorApp;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using LifetimeTests.Components;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalonia.LifetimeTests;

/// <summary>
/// https://github.com/ScarletKuro/WpfIssueThreading (dotnet/maui#34855) for the Avalonia example's host
/// (<c>BlazorViewHost</c> in a <c>NativeControlHost</c>): a component keeps working in the background and calls
/// InvokeAsync/DispatchExceptionAsync after its window is gone, while the application keeps running. Every scenario must
/// finish without an exception reaching the view's or Avalonia's unhandled exception handlers. Also checks what is
/// specific to Avalonia: awaited component disposal before the window closes, layout-driven resizing, and dispatching
/// through Avalonia's dispatcher. Exit code 0 means all scenarios passed.
/// </summary>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		var exitCode = AppBuilder.Configure(() => new TestApp(new LifetimeTestRunner(args)))
			.UsePlatformDetect()
			.LogToTrace()
			.StartWithClassicDesktopLifetime(args);
		return ShutdownFlood.Verify() ? exitCode : 1;
	}
}

/// <summary>
/// Work dispatched through a view while the app shuts down: every invocation must run or fault, none may stay pending.
/// </summary>
internal static class ShutdownFlood
{
	private static readonly ConcurrentQueue<Task> s_invocations = new();
	private static int s_ran;
	private static volatile bool s_stop;

	public static void Start(Microsoft.AspNetCore.Components.Dispatcher dispatcher) => new Thread(() =>
	{
		while (!s_stop)
		{
			s_invocations.Enqueue(dispatcher.InvokeAsync(() => { Interlocked.Increment(ref s_ran); }));
		}
	}) { IsBackground = true }.Start();

	// After the UI loop has ended: keep dispatching for a moment, then account for every invocation.
	public static bool Verify()
	{
		Thread.Sleep(300);
		s_stop = true;
		Thread.Sleep(100);
		var all = s_invocations.ToArray();
		SpinWait.SpinUntil(() => all.All(t => t.IsCompleted), TimeSpan.FromSeconds(10));
		var ran = all.Count(t => t.Status == TaskStatus.RanToCompletion);
		var faulted = all.Count(t => t.IsFaulted && t.Exception!.InnerException is InvalidOperationException);
		var pending = all.Count(t => !t.IsCompleted);
		var passed = all.Length > 0 && pending == 0 && ran + faulted == all.Length && ran == Volatile.Read(ref s_ran) && faulted > 0;
		Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  Shutdown while work is dispatched through a view");
		Console.WriteLine($"      {all.Length} invocations: {ran} ran, {faulted} faulted, {pending} pending, {all.Length - ran - faulted - pending} other");
		return passed;
	}
}

internal sealed class TestApp(LifetimeTestRunner runner) : Application
{
	public override void Initialize()
	{
		RequestedThemeVariant = ThemeVariant.Light;
		Styles.Add(new FluentTheme());
	}

	public override void OnFrameworkInitializationCompleted()
	{
		var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
		desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

		// What a real app sees as a crash (or its global error handling).
		Dispatcher.UIThread.UnhandledException += (_, e) =>
		{
			if (ReferenceEquals(e.Exception, Scenarios.ExpectedUnhandled))
			{
				Scenarios.ExpectedUnhandledReported.TrySetResult();
			}
			else
			{
				runner.ReportFatal("Dispatcher.UnhandledException", e.Exception);
			}

			e.Handled = true;
		};

		var services = new ServiceCollection();
		services.AddLogging(b => b.AddProvider(runner.LoggerProvider).SetMinimumLevel(LogLevel.Error));
		services.AddWin32AotBlazorWebView();
		var provider = services.BuildServiceProvider();

		Dispatcher.UIThread.Post(async () =>
		{
			var scenarios = new Scenarios(runner, provider);
			var exitCode = await runner.RunAsync(scenarios.All());

			// Shut down with a view still open while a worker keeps dispatching through it (verified in Main).
			var view = await scenarios.OpenViewForShutdownAsync();
			ShutdownFlood.Start(view.Dispatcher);
			await Task.Delay(200);
			desktop.Shutdown(exitCode);
		});

		base.OnFrameworkInitializationCompleted();
	}
}

internal sealed class Scenarios(LifetimeTestRunner runner, IServiceProvider services)
{
	public IEnumerable<(string Name, Func<Task> Run)> All()
	{
		foreach (var throwException in new[] { true, false })
		{
			var suffix = throwException ? "exception thrown" : "no exception";
			yield return ($"Close window, {suffix}", () => CloseWindow(throwException));
			yield return ($"BlazorViewHost removed from the window, window stays open, {suffix}", () => RemoveHost(throwException));
			yield return ($"Plain Window closed without awaiting disposal, {suffix}", () => ClosePlainWindow(throwException));
		}

		yield return ("Rapid open/close x10, exception thrown", () => RapidOpenClose(count: 10));
		yield return ("Close waits for asynchronous component disposal", CloseAwaitsDisposal);
		yield return ("Resize follows Avalonia layout", () => Resize(constrained: false));
		yield return ("Resize follows Avalonia layout, window smaller than its preferred size", () => Resize(constrained: true));
		yield return ("Dispatching through Avalonia's dispatcher", DispatchThroughAvalonia);
		yield return ("Dispatched work runs during a modal move loop", DispatchDuringModalLoop);
		yield return ("Exceptions from work posted through the adapter reach Avalonia's handler", PostedExceptionReachesAvalonia);
	}

	/// <summary>Reported to <see cref="Dispatcher.UnhandledException"/> by the scenario that expects it.</summary>
	public static Exception? ExpectedUnhandled { get; private set; }

	public static TaskCompletionSource ExpectedUnhandledReported { get; private set; } = new();

	// What BlazorWebView does with an exception its UnhandledException handlers leave unhandled.
	private async Task PostedExceptionReachesAvalonia()
	{
		var expected = new InvalidOperationException("Expected unhandled exception");
		ExpectedUnhandledReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ExpectedUnhandled = expected;
		try
		{
			await Task.Run(() => new AvaloniaUiDispatcher(Dispatcher.UIThread).Post(() => throw expected));
			await ExpectedUnhandledReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally
		{
			ExpectedUnhandled = null;
		}
	}

	public async Task<BlazorWebView> OpenViewForShutdownAsync()
	{
		Probe.Reset();
		var host = CreateHost(view => view.RootComponents.Add<Probe>("#app"));
		new Window { Title = "Avalonia.LifetimeTests (shutdown)", Width = 640, Height = 360, Content = host }.Show();
		await LifetimeTestRunner.WaitUntil(() => Probe.Rendered, TimeSpan.FromSeconds(30), "the root component to render");
		return host.View;
	}

	// The supported path: BlazorWindow cancels the first close, awaits the view's disposal, then closes.
	private async Task CloseWindow(bool throwException)
	{
		var window = CreateWindow(throwException);
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		await runner.WaitForBackgroundWorkFinished(1);
	}

	// NativeControlHost destroys its child when detached; the host then starts disposal without awaiting it.
	private async Task RemoveHost(bool throwException)
	{
		var window = CreateWindow(throwException);
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Content = null;
		await runner.WaitForBackgroundWorkFinished(1);
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
	}

	// Host misuse: a window without BlazorWindow's close handling, destroyed while disposal is still running.
	private async Task ClosePlainWindow(bool throwException)
	{
		var closed = false;
		var window = new Window { Title = "Avalonia.LifetimeTests (plain window)", Width = 640, Height = 360, Content = CreateHost(AppComponent(throwException)) };
		window.Closed += (_, _) => closed = true;
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Close();
		await LifetimeTestRunner.WaitUntil(() => closed, TimeSpan.FromSeconds(15), "window to close");
		await runner.WaitForBackgroundWorkFinished(1);
	}

	private async Task RapidOpenClose(int count)
	{
		var windows = new List<BlazorWindow>();
		for (var i = 0; i < count; i++)
		{
			var window = CreateWindow(throwException: true);
			window.Show();
			await runner.WaitForBackgroundWorkStarted(i + 1);
			window.Close();
			windows.Add(window);
		}

		await Task.WhenAll(windows.Select(w => w.ClosedTask)).WaitAsync(TimeSpan.FromSeconds(30));
		await runner.WaitForBackgroundWorkFinished(count);
	}

	private async Task CloseAwaitsDisposal()
	{
		Probe.Reset();
		var window = CreateProbeWindow();
		var disposedWhenClosed = false;
		window.Closed += (_, _) => disposedWhenClosed = Probe.Disposed;
		window.Show();
		await LifetimeTestRunner.WaitUntil(() => Probe.Rendered, TimeSpan.FromSeconds(30), "the root component to render");
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		Require(disposedWhenClosed, "The window closed before the root component's asynchronous disposal completed.");
	}

	private async Task Resize(bool constrained)
	{
		var window = CreateProbeWindow();
		window.Width = 1280;
		window.Height = 800;
		if (constrained)
		{
			// A desktop that can't fit the window's preferred size (as on small CI screens).
			window.MinWidth = 1000;
			window.MinHeight = 520;
			window.MaxWidth = 1000;
			window.MaxHeight = 650;
		}

		window.Show();
		try
		{
			await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
			await ResizeChecks.VerifyAsync(window);
		}
		finally
		{
			window.Close();
			await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		}
	}

	private async Task DispatchThroughAvalonia()
	{
		var window = CreateProbeWindow();
		window.Show();
		try
		{
			await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
			var dispatcher = window.BlazorHost.View.Dispatcher;
			await Task.Run(async () =>
			{
				await dispatcher.InvokeAsync(AssertOnAvaloniaContext);
				Require(await dispatcher.InvokeAsync(() => { AssertOnAvaloniaContext(); return 42; }) == 42, "Synchronous result");
				await dispatcher.InvokeAsync(async () =>
				{
					await Task.Delay(20);
					AssertOnAvaloniaContext();
				});
				Require(await dispatcher.InvokeAsync(async () =>
				{
					await Task.Delay(20);
					AssertOnAvaloniaContext();

					// On the UI thread, work runs inline.
					var ranInline = false;
					var inline = dispatcher.InvokeAsync(() => ranInline = true);
					Require(ranInline && inline.IsCompleted, "Work dispatched on the UI thread runs inline.");
					await inline;
					return 43;
				}) == 43, "Asynchronous result");
			});
		}
		finally
		{
			window.Close();
			await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		}

		static void AssertOnAvaloniaContext()
		{
			Dispatcher.UIThread.VerifyAccess();
			Require(SynchronizationContext.Current is AvaloniaSynchronizationContext, "Dispatched work runs on Avalonia's synchronization context.");
		}
	}

	// Moving or resizing a window runs a modal message loop inside DefWindowProc, which Avalonia's dispatcher doesn't
	// control. Work dispatched from another thread must still run while it's active.
	private async Task DispatchDuringModalLoop()
	{
		var window = CreateProbeWindow();
		window.Show();
		try
		{
			await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
			var hwnd = window.TryGetPlatformHandle()!.Handle;
			var dispatcher = window.BlazorHost.View.Dispatcher;
			var inModalLoop = false;
			var ranInModalLoop = false;
			var worker = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(300);
					await dispatcher.InvokeAsync(() => { ranInModalLoop = Volatile.Read(ref inModalLoop); }).WaitAsync(TimeSpan.FromSeconds(5));
				}
				finally
				{
					// End the keyboard move loop.
					Native.PostMessage(hwnd, Native.WM_KEYDOWN, Native.VK_ESCAPE, 0);
					await Task.Delay(2000);
					if (Volatile.Read(ref inModalLoop))
					{
						Native.PostMessage(hwnd, Native.WM_CANCELMODE, 0, 0);
					}
				}
			});

			var inLoop = System.Diagnostics.Stopwatch.StartNew();
			Volatile.Write(ref inModalLoop, true);
			Native.SendMessage(hwnd, Native.WM_SYSCOMMAND, Native.SC_MOVE, 0);
			Volatile.Write(ref inModalLoop, false);
			inLoop.Stop();
			await worker;
			Require(inLoop.ElapsedMilliseconds >= 250, $"The window didn't enter its modal move loop ({inLoop.ElapsedMilliseconds} ms).");
			Require(ranInModalLoop, "Work dispatched from another thread didn't run during the modal move loop.");
		}
		finally
		{
			window.Close();
			await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		}
	}

	private BlazorWindow CreateWindow(bool throwException) => CreateWindow(AppComponent(throwException));

	private BlazorWindow CreateProbeWindow() => CreateWindow(view => view.RootComponents.Add<Probe>("#app"));

	private BlazorWindow CreateWindow(Action<BlazorWebView> addComponents)
	{
		var window = new BlazorWindow(services, view => Configure(view, addComponents)) { Title = "Avalonia.LifetimeTests", Width = 640, Height = 360 };
		Observe(window.BlazorHost);
		return window;
	}

	private BlazorViewHost CreateHost(Action<BlazorWebView> addComponents)
	{
		var host = new BlazorViewHost(services, view => Configure(view, addComponents));
		Observe(host);
		return host;
	}

	// The example reports its own failures (WebView2 startup, native window messages, disposal) in its status area.
	private void Observe(BlazorViewHost host) => host.StatusChanged += status =>
	{
		if (status.StartsWith("Error:", StringComparison.Ordinal))
		{
			runner.ReportFatal("BlazorViewHost", new InvalidOperationException(status));
		}
	};

	private Action<BlazorWebView> AppComponent(bool throwException) =>
		view => view.RootComponents.Add<App>("#app", runner.Parameters(throwException));

	private void Configure(BlazorWebView view, Action<BlazorWebView> addComponents)
	{
		addComponents(view);

		// Reaching this handler is the bug: an exception escaped to the host. The example's own handler marks it handled.
		view.UnhandledException += (_, e) => runner.ReportFatal("BlazorWebView.UnhandledException", e.Exception);
	}

	internal static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}

internal static partial class Native
{
	public const uint WM_KEYDOWN = 0x0100;
	public const uint WM_SYSCOMMAND = 0x0112;
	public const uint WM_CANCELMODE = 0x001F;
	public const nint SC_MOVE = 0xF010;
	public const nint VK_ESCAPE = 0x1B;

	[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
	public static partial nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

	[LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
