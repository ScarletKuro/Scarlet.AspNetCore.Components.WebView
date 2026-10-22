using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DirectN.Extensions.Utilities;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Win32Aot.LifetimeTests.Components;
using Functions = DirectN.Functions;
using HWND = DirectN.HWND;
using PEEK_MESSAGE_REMOVE_TYPE = DirectN.PEEK_MESSAGE_REMOVE_TYPE;
using WINDOW_STYLE = DirectN.WINDOW_STYLE;

namespace Win32Aot.LifetimeTests;

/// <summary>
/// Port of https://github.com/ScarletKuro/WpfIssueThreading (dotnet/maui#34855) to the Win32Aot host.
/// A component keeps working in the background and calls InvokeAsync/DispatchExceptionAsync after its window is gone,
/// while the application keeps running (like WPF's ShutdownMode="OnExplicitShutdown"). Every scenario must finish
/// without an unhandled exception. Exit code 0 means all scenarios passed.
/// </summary>
/// <remarks>Usage: <c>Win32Aot.LifetimeTests [delayMs] [--hold ms] [--verbose]</c></remarks>
internal static class Program
{
	private static readonly ConcurrentQueue<string> s_fatal = new();
	private static readonly ConcurrentQueue<string> s_unobserved = new();
	private static readonly ConcurrentQueue<string> s_logged = new();
	private static bool s_verbose;
	private static TimeSpan s_hold = TimeSpan.FromMilliseconds(1000);

	[STAThread]
	private static int Main(string[] args)
	{
		var delay = args.Length > 0 && int.TryParse(args[0], out var d) ? d : 3000;
		s_verbose = Array.IndexOf(args, "--verbose") >= 0;
		var holdIndex = Array.IndexOf(args, "--hold");
		if (holdIndex >= 0 && holdIndex + 1 < args.Length && int.TryParse(args[holdIndex + 1], out var hold))
		{
			s_hold = TimeSpan.FromMilliseconds(hold);
		}

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			// The process terminates after this handler; print immediately.
			Console.WriteLine($"FATAL AppDomain.UnhandledException: {e.ExceptionObject}");
			Console.Out.Flush();
		};
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			s_unobserved.Enqueue(e.Exception.GetBaseException().Message);
			e.SetObserved();
		};

		var services = new ServiceCollection();
		services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(s_logged)).SetMinimumLevel(LogLevel.Error));
		services.AddWin32AotBlazorWebView();
		using var serviceProvider = services.BuildServiceProvider();

		using var app = new Application { ExitOnLastWindowRemoved = false };

		var scenarios = new (string Name, Action<IServiceProvider, int> Run)[]
		{
			("Close window (WM_CLOSE), exception thrown", (sp, ms) => CloseWindow(sp, ms, throwException: true)),
			("Close window (WM_CLOSE), no exception", (sp, ms) => CloseWindow(sp, ms, throwException: false)),
			("Destroy window without WM_CLOSE, exception thrown", (sp, ms) => DestroyWindow(sp, ms, throwException: true)),
			("Destroy window without WM_CLOSE, no exception", (sp, ms) => DestroyWindow(sp, ms, throwException: false)),
			("Parent destroyed, view never disposed, exception thrown", (sp, ms) => NeverDisposed(sp, ms, throwException: true)),
			("Parent destroyed, view never disposed, no exception", (sp, ms) => NeverDisposed(sp, ms, throwException: false)),
			("Rapid open/close x10, exception thrown", (sp, ms) => RapidOpenClose(sp, ms, count: 10)),
		};

		var results = new List<(string Name, string Outcome, string Detail)>();
		foreach (var (name, run) in scenarios)
		{
			Console.WriteLine($"--- {name}");
			s_fatal.Clear();
			s_unobserved.Clear();
			s_logged.Clear();
			BackgroundWork.Reset();

			var stopwatch = Stopwatch.StartNew();
			string? error = null;
			try
			{
				run(serviceProvider, delay);
			}
			catch (Exception ex)
			{
				error = $"{ex.GetType().Name}: {ex.Message}";
			}

			// Let late continuations and finalizers surface their exceptions.
			Pump.For(TimeSpan.FromMilliseconds(500));
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			Pump.For(TimeSpan.FromMilliseconds(200));

			var failures = new List<string>();
			if (error is not null) failures.Add(error);
			failures.AddRange(s_fatal);

			// Not failures: the component's own fire-and-forget task observes the disposed renderer, and ErrorBoundary
			// logs the intentional exception.
			var info = new List<string>();
			if (!s_unobserved.IsEmpty) info.Add($"unobserved task exceptions: {string.Join(" | ", s_unobserved)}");
			if (!s_logged.IsEmpty) info.Add($"error logs: {string.Join(" | ", s_logged)}");

			var outcome = failures.Count == 0 ? "PASS" : "FAIL";
			var detail = string.Join("; ", failures);
			if (info.Count > 0) detail = (detail.Length > 0 ? detail + "; " : "") + string.Join("; ", info);
			results.Add((name, outcome, detail));
			Console.WriteLine($"    {outcome} in {stopwatch.Elapsed.TotalSeconds:N1}s (background work started {BackgroundWork.Started}, finished {BackgroundWork.Finished})");
		}

		Console.WriteLine();
		Console.WriteLine("==== Summary");
		foreach (var (name, outcome, detail) in results)
		{
			Console.WriteLine($"{outcome}  {name}{(detail.Length > 0 ? $"\n      {detail}" : "")}");
		}

		return results.TrueForAll(r => r.Outcome == "PASS") ? 0 : 1;
	}

	// The supported path: BlazorWebViewWindow disposes the view before the window is destroyed.
	private static void CloseWindow(IServiceProvider services, int delay, bool throwException)
	{
		var window = OpenWindow(services, delay, throwException);
		WaitForBackgroundWork(started: 1);
		Pump.For(s_hold);
		window.Close();
		Pump.Until(() => window.IsDisposed, TimeSpan.FromSeconds(15), "window to close");
		WaitForBackgroundWork(finished: 1, delay);
	}

	// The window is destroyed without WM_CLOSE (e.g. a parent window or the app tears it down); disposal of the view
	// is only started on a best-effort basis.
	private static void DestroyWindow(IServiceProvider services, int delay, bool throwException)
	{
		var window = OpenWindow(services, delay, throwException);
		WaitForBackgroundWork(started: 1);
		Pump.For(s_hold);
		window.Dispose();
		WaitForBackgroundWork(finished: 1, delay);
	}

	// Host misuse matching "DisposeAsync() NOT called" in the original repro: a raw BlazorWebView whose parent window
	// is destroyed and which is never disposed. Background work then talks to a WebView2 whose parent is gone.
	private static void NeverDisposed(IServiceProvider services, int delay, bool throwException)
	{
		var parent = new Window("Win32Aot.LifetimeTests (raw parent)", WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
		var view = new BlazorWebView(parent.Handle.Value) { HostPage = @"wwwroot\index.html", Services = services };
		Configure(view, delay, throwException);
		view.Bounds = parent.ClientRect;
		parent.Show();
		var start = view.StartAsync();

		WaitForBackgroundWork(started: 1);
		if (start.IsFaulted)
		{
			throw start.Exception!.GetBaseException();
		}

		Pump.For(s_hold);
		parent.Dispose();
		WaitForBackgroundWork(finished: 1, delay);
		GC.KeepAlive(view);
	}

	private static void RapidOpenClose(IServiceProvider services, int delay, int count)
	{
		var windows = new List<BlazorWebViewWindow>();
		for (var i = 0; i < count; i++)
		{
			var window = OpenWindow(services, delay, throwException: true);
			WaitForBackgroundWork(started: i + 1);
			window.Close();
			windows.Add(window);
		}

		Pump.Until(() => windows.TrueForAll(w => w.IsDisposed), TimeSpan.FromSeconds(30), "windows to close");
		WaitForBackgroundWork(finished: count, delay);
	}

	private static BlazorWebViewWindow OpenWindow(IServiceProvider services, int delay, bool throwException)
	{
		var window = new BlazorWebViewWindow("Win32Aot.LifetimeTests");
		window.BlazorWebView.HostPage = @"wwwroot\index.html";
		window.BlazorWebView.Services = services;
		Configure(window.BlazorWebView, delay, throwException);
		window.ResizeClient(640, 360);
		window.Show();
		return window;
	}

	private static void Configure(BlazorWebView view, int delay, bool throwException)
	{
		view.RootComponents.Add<App>("#app", new Dictionary<string, object?>
		{
			[nameof(App.ThrowException)] = throwException,
			[nameof(App.Delay)] = delay,
		});

		// Reaching this handler is the bug: it means an exception escaped to the host (by default it would crash).
		view.UnhandledException += (_, e) =>
		{
			s_fatal.Enqueue($"BlazorWebView.UnhandledException: {e.Exception.GetType().Name}: {e.Exception.Message}");
			if (s_verbose)
			{
				Console.WriteLine(e.Exception);
			}

			e.Handled = true;
		};
	}

	private static void WaitForBackgroundWork(int started) =>
		Pump.Until(() => BackgroundWork.Started >= started, TimeSpan.FromSeconds(30), "background work to start");

	private static void WaitForBackgroundWork(int finished, int delay) =>
		Pump.Until(() => BackgroundWork.Finished >= finished, TimeSpan.FromMilliseconds(delay + 15000), "background work to finish");

	private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider, ILogger
	{
		public ILogger CreateLogger(string categoryName) => this;

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			=> sink.Enqueue(exception is null ? formatter(state, exception) : $"{exception.GetType().Name}: {exception.Message}");

		public void Dispose()
		{
		}
	}
}

/// <summary>
/// Runs the Win32 message loop until a condition is met, so scenarios can be written sequentially.
/// </summary>
internal static class Pump
{
	public static void Until(Func<bool> condition, TimeSpan timeout, string what)
	{
		var deadline = Stopwatch.StartNew();
		while (!condition())
		{
			if (deadline.Elapsed > timeout)
			{
				throw new TimeoutException($"Timed out waiting for {what}.");
			}

			PumpOnce();
		}
	}

	public static void For(TimeSpan duration)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < duration)
		{
			PumpOnce();
		}
	}

	private static void PumpOnce()
	{
		var any = false;
		while (Functions.PeekMessageW(out var msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
		{
			any = true;
			Functions.TranslateMessage(msg);
			Functions.DispatchMessageW(msg);
		}

		if (!any)
		{
			Thread.Sleep(5);
		}
	}
}
