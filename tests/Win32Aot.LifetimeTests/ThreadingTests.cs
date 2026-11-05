using System;
using System.Threading;
using System.Threading.Tasks;
using DirectN.Extensions.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Scarlet.DirectN.Threading;

namespace Win32Aot.LifetimeTests;

internal static class ThreadingTests
{
	public static void Run()
	{
		var initialQueue = DirectNMessageQueue.Current;
		Expect<ArgumentException>(() => _ = new BlazorWebView(0));
		Require(ReferenceEquals(DirectNMessageQueue.Current, initialQueue),
			"An invalid default constructor call must not create a UI queue.");

		using var parent = new Window("Threading contract tests");
		Expect<ArgumentNullException>(() => _ = new BlazorWebView(parent.Handle.Value, null!));
		Require(ReferenceEquals(DirectNMessageQueue.Current, initialQueue),
			"An explicit null integration must not create a default adapter.");
		var previousContext = SynchronizationContext.Current;
		try
		{
			using var threading = new TrackingThreading();
			threading.AllowAccess = false;
			Expect<ArgumentException>(() => _ = new BlazorWebView(parent.Handle.Value, threading));
			Require(threading.DisposeCount == 0, "A rejected integration remains owned by its caller.");
			threading.AllowAccess = true;

			SynchronizationContext.SetSynchronizationContext(null);
			var view = new BlazorWebView(parent.Handle.Value, threading);
			Require(ReferenceEquals(SynchronizationContext.Current, threading.SynchronizationContext),
				"The integration supplies the fallback UI context.");

			// Make the integration deny access while its underlying dispatcher still allows it. This proves
			// that the view's guards use the integration's contract rather than a separate thread-id check.
			threading.AllowAccess = false;
			Expect<InvalidOperationException>(() => _ = view.StartAsync());
			Expect<InvalidOperationException>(() => _ = view.DisposeAsync());
			threading.AllowAccess = true;

			var wrongThread = Task.Run(() => Expect<InvalidOperationException>(() => _ = view.StartAsync()));
			Pump.Until(() => wrongThread.IsCompleted, TimeSpan.FromSeconds(5), "cross-thread access check");
			wrongThread.GetAwaiter().GetResult();

			var expected = new InvalidOperationException("Expected default threading failure");
			var reported = 0;
			view.UnhandledException += (_, e) =>
			{
				Require(ReferenceEquals(e.Exception, expected), "The view receives the dispatched exception.");
				Require(threading.CheckAccess(), "Exceptions are reported on the UI thread.");
				reported++;
				e.Handled = true;
			};
			var failed = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => throw expected)));
			Pump.Until(() => failed.IsCompleted && reported == 1, TimeSpan.FromSeconds(5), "handled dispatcher failure");
			Expect<InvalidOperationException>(() => failed.GetAwaiter().GetResult());

			view.DisposeAsync().GetAwaiter().GetResult();
			view.DisposeAsync().GetAwaiter().GetResult();
			Require(threading.DisposeCount == 1, "The view disposes its integration exactly once.");
			Require(DirectNMessageQueue.Current is { IsClosed: false }, "Disposal leaves the shared queue alive.");

			var continued = false;
			var work = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => continued = true)));
			Pump.Until(() => work.IsCompleted, TimeSpan.FromSeconds(5), "dispatch after integration disposal");
			work.GetAwaiter().GetResult();
			Require(continued, "Dispatching remains usable after the view's integration is disposed.");

			failed = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => throw expected)));
			Pump.Until(() => failed.IsCompleted, TimeSpan.FromSeconds(5), "late dispatcher failure");
			Expect<InvalidOperationException>(() => failed.GetAwaiter().GetResult());
			Require(reported == 1, "Late failures fault the caller without reporting to the disposed view.");

			// A second view on this thread still uses the same shared UI infrastructure.
			var existingContext = new SynchronizationContext();
			SynchronizationContext.SetSynchronizationContext(existingContext);
			var sibling = new BlazorWebView(parent.Handle.Value);
			Require(ReferenceEquals(SynchronizationContext.Current, existingContext), "An existing host context is preserved.");
			sibling.DisposeAsync().GetAwaiter().GetResult();
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previousContext);
		}
	}

	private static void Expect<T>(Action action) where T : Exception
	{
		try { action(); }
		catch (T) { return; }
		throw new InvalidOperationException($"Expected {typeof(T).Name}.");
	}

	private static void Require(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private sealed class TrackingThreading : IBlazorWebViewThreading
	{
		private readonly DirectNBlazorWebViewThreading _inner = new();
		public bool AllowAccess { get; set; } = true;
		public int DisposeCount { get; private set; }
		public Dispatcher Dispatcher => _inner.Dispatcher;
		public SynchronizationContext SynchronizationContext => _inner.SynchronizationContext;
		public bool CheckAccess() => AllowAccess && _inner.CheckAccess();
		public event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException
		{
			add => _inner.UnhandledException += value;
			remove => _inner.UnhandledException -= value;
		}
		public void Dispose()
		{
			DisposeCount++;
			_inner.Dispose();
		}
	}
}
