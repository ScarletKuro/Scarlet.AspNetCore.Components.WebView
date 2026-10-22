using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LifetimeTests.Components;

/// <summary>
/// Runs lifetime scenarios on the UI thread of a host (WinForms, WPF) whose synchronization context keeps pumping
/// messages while scenarios await. A scenario fails when it throws or when the host reports an exception that escaped
/// to it (<see cref="ReportFatal"/>), which is what would crash a real app.
/// </summary>
/// <remarks>Command line: <c>[delayMs] [--hold ms] [--verbose]</c></remarks>
public sealed class LifetimeTestRunner
{
	private readonly ConcurrentQueue<string> _fatal = new();
	private readonly ConcurrentQueue<string> _unobserved = new();
	private readonly ConcurrentQueue<string> _logged = new();
	private readonly bool _verbose;

	public LifetimeTestRunner(string[] args)
	{
		Delay = args.Length > 0 && int.TryParse(args[0], out var delay) ? delay : 3000;
		_verbose = Array.IndexOf(args, "--verbose") >= 0;
		var holdIndex = Array.IndexOf(args, "--hold");
		if (holdIndex >= 0 && holdIndex + 1 < args.Length && int.TryParse(args[holdIndex + 1], out var hold))
		{
			Hold = TimeSpan.FromMilliseconds(hold);
		}

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			// The process terminates after this handler; print immediately.
			Console.WriteLine($"FATAL AppDomain.UnhandledException: {e.ExceptionObject}");
			Console.Out.Flush();
		};
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			_unobserved.Enqueue(e.Exception.GetBaseException().Message);
			e.SetObserved();
		};
	}

	/// <summary>How long the background work in <see cref="BackgroundWork"/> runs.</summary>
	public int Delay { get; }

	/// <summary>How long a scenario keeps the window open after the background work started (so the page renders).</summary>
	public TimeSpan Hold { get; } = TimeSpan.FromMilliseconds(1000);

	/// <summary>Captures error logs; they are reported but are not failures.</summary>
	public ILoggerProvider LoggerProvider => new CapturingLoggerProvider(_logged);

	/// <summary>Records an exception that reached the host's unhandled exception handler.</summary>
	public void ReportFatal(string source, Exception exception)
	{
		_fatal.Enqueue($"{source}: {exception.GetType().Name}: {exception.Message}");
		if (_verbose)
		{
			Console.WriteLine(exception);
		}
	}

	public async Task<int> RunAsync(IEnumerable<(string Name, Func<Task> Run)> scenarios)
	{
		var results = new List<(string Name, bool Passed, string Detail)>();
		foreach (var (name, run) in scenarios)
		{
			Console.WriteLine($"--- {name}");
			_fatal.Clear();
			_unobserved.Clear();
			_logged.Clear();
			BackgroundWork.Reset();

			var stopwatch = Stopwatch.StartNew();
			string? error = null;
			try
			{
				await run();
			}
			catch (Exception ex)
			{
				error = $"{ex.GetType().Name}: {ex.Message}";
				if (_verbose)
				{
					Console.WriteLine(ex);
				}
			}

			// Let late continuations and finalizers surface their exceptions.
			await Task.Delay(500);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			await Task.Delay(200);

			var failures = new List<string>();
			if (error is not null) failures.Add(error);
			failures.AddRange(_fatal);

			// Not failures: the component's own fire-and-forget task observes the disposed renderer, and ErrorBoundary
			// logs the intentional exception.
			var info = new List<string>();
			if (!_unobserved.IsEmpty) info.Add($"unobserved task exceptions: {string.Join(" | ", _unobserved)}");
			if (!_logged.IsEmpty) info.Add($"error logs: {string.Join(" | ", _logged)}");

			var passed = failures.Count == 0;
			var detail = string.Join("; ", failures);
			if (info.Count > 0) detail = (detail.Length > 0 ? detail + "; " : "") + string.Join("; ", info);
			results.Add((name, passed, detail));
			Console.WriteLine($"    {(passed ? "PASS" : "FAIL")} in {stopwatch.Elapsed.TotalSeconds:N1}s (background work started {BackgroundWork.Started}, finished {BackgroundWork.Finished})");
		}

		Console.WriteLine();
		Console.WriteLine("==== Summary");
		foreach (var (name, passed, detail) in results)
		{
			Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? $"\n      {detail}" : "")}");
		}

		return results.TrueForAll(r => r.Passed) ? 0 : 1;
	}

	public static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, string what)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition())
		{
			if (stopwatch.Elapsed > timeout)
			{
				throw new TimeoutException($"Timed out waiting for {what}.");
			}

			await Task.Delay(10);
		}
	}

	public Task WaitForBackgroundWorkStarted(int count) =>
		WaitUntil(() => BackgroundWork.Started >= count, TimeSpan.FromSeconds(30), "background work to start");

	public Task WaitForBackgroundWorkFinished(int count) =>
		WaitUntil(() => BackgroundWork.Finished >= count, TimeSpan.FromMilliseconds(Delay + 15000), "background work to finish");

	/// <summary>Parameters for <see cref="App"/>.</summary>
	public Dictionary<string, object?> Parameters(bool throwException) => new()
	{
		[nameof(App.ThrowException)] = throwException,
		[nameof(App.Delay)] = Delay,
	};

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
