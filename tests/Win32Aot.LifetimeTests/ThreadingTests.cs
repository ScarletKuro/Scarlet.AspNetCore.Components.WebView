using System;
using System.Threading;
using System.Threading.Tasks;
using DirectN.Extensions.Utilities;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Scarlet.DirectN.Threading;

namespace Win32Aot.LifetimeTests;

/// <summary>
/// <c>new BlazorWebView(hwnd, dispatcher)</c>: a view whose work goes through a host framework's dispatcher.
/// </summary>
internal static class ThreadingTests
{
	public static void Run()
	{
		var initialQueue = DirectNMessageQueue.Current;
		Expect<ArgumentException>(() => _ = new BlazorWebView(0));
		Require(ReferenceEquals(DirectNMessageQueue.Current, initialQueue), "An invalid parent must not create a UI queue.");

		using var parent = new Window("Threading tests");
		Expect<ArgumentNullException>(() => _ = new BlazorWebView(parent.Handle.Value, null!));
		Require(ReferenceEquals(DirectNMessageQueue.Current, initialQueue), "A null dispatcher must not fall back to a UI queue.");

		var queue = DirectNMessageQueue.GetOrCreateForCurrentThread();
		var dispatcher = new HostDispatcher(queue) { AllowAccess = false };
		Expect<ArgumentException>(() => _ = new BlazorWebView(parent.Handle.Value, dispatcher));
		dispatcher.AllowAccess = true;

		var previousContext = SynchronizationContext.Current;
		try
		{
			// Without a context on the thread, the view installs one that posts through the host's dispatcher.
			SynchronizationContext.SetSynchronizationContext(null);
			var view = new BlazorWebView(parent.Handle.Value, dispatcher);
			var context = SynchronizationContext.Current;
			Require(context is not null and not DirectNSynchronizationContext, "The view installs a context for the host's dispatcher.");
			var posted = false;
			var enqueued = dispatcher.Enqueued;
			context!.Post(_ => posted = true, null);
			Pump.Until(() => posted, TimeSpan.FromSeconds(5), "work posted through the view's context");
			Require(dispatcher.Enqueued == enqueued + 1, "The view's context posts through the host's dispatcher.");

			// The view's thread checks use the host's dispatcher.
			dispatcher.AllowAccess = false;
			Expect<InvalidOperationException>(() => _ = view.StartAsync());
			Expect<InvalidOperationException>(() => _ = view.DisposeAsync());
			dispatcher.AllowAccess = true;
			var wrongThread = Task.Run(() => Expect<InvalidOperationException>(() => _ = view.StartAsync()));
			Pump.Until(() => wrongThread.IsCompleted, TimeSpan.FromSeconds(5), "cross-thread access check");
			wrongThread.GetAwaiter().GetResult();

			// Dispatched work goes through the host's dispatcher, and its failures reach the view.
			var expected = new InvalidOperationException("Expected dispatcher failure");
			var reported = 0;
			var handle = true;
			view.UnhandledException += (_, e) =>
			{
				Require(ReferenceEquals(e.Exception, expected), "The view receives the dispatched exception.");
				Require(dispatcher.CheckAccess(), "Exceptions are reported on the UI thread.");
				reported++;
				e.Handled = handle;
			};
			enqueued = dispatcher.Enqueued;
			var failed = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => throw expected)));
			Pump.Until(() => failed.IsCompleted && reported == 1, TimeSpan.FromSeconds(5), "handled dispatcher failure");
			Expect<InvalidOperationException>(() => failed.GetAwaiter().GetResult());
			Require(dispatcher.Enqueued > enqueued, "Work from other threads goes through the host's dispatcher.");

			// Left unhandled, the exception is rethrown through the host's dispatcher (here onto the queue's handler),
			// and isn't reported to the view a second time.
			handle = false;
			Exception? rethrown = null;
			EventHandler<DirectNUnhandledExceptionEventArgs> hostPolicy = (_, e) =>
			{
				rethrown = e.Exception;
				e.Handled = true;
			};
			queue.UnhandledException += hostPolicy;
			try
			{
				failed = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => throw expected)));
				Pump.Until(() => failed.IsCompleted && rethrown is not null, TimeSpan.FromSeconds(5), "unhandled dispatcher failure");
				Require(ReferenceEquals(rethrown, expected), "An unhandled exception follows the host dispatcher's unhandled path.");
				Require(reported == 2, "An unhandled exception is reported to the view once.");
			}
			finally
			{
				queue.UnhandledException -= hostPolicy;
			}

			view.DisposeAsync().GetAwaiter().GetResult();
			view.DisposeAsync().GetAwaiter().GetResult();

			// The host's dispatcher outlives the view: dispatching still works, and late failures only fault the caller.
			var continued = false;
			var work = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => continued = true)));
			Pump.Until(() => work.IsCompleted, TimeSpan.FromSeconds(5), "dispatch after disposal");
			work.GetAwaiter().GetResult();
			Require(continued, "Dispatching still works after the view is disposed.");
			rethrown = null;
			queue.UnhandledException += hostPolicy;
			try
			{
				failed = Task.Run(() => view.Dispatcher.InvokeAsync((Action)(() => throw expected)));
				Pump.Until(() => failed.IsCompleted, TimeSpan.FromSeconds(5), "late dispatcher failure");
				Pump.For(TimeSpan.FromMilliseconds(200));
				Expect<InvalidOperationException>(() => failed.GetAwaiter().GetResult());
				Require(reported == 2 && rethrown is null, "Late failures fault the caller without being reported.");
			}
			finally
			{
				queue.UnhandledException -= hostPolicy;
			}

			// Passing the thread's queue is the same as the default constructor, which also reports the queue's own
			// failures to the view, and keeps an existing context.
			var existingContext = new SynchronizationContext();
			SynchronizationContext.SetSynchronizationContext(existingContext);
			var sibling = new BlazorWebView(parent.Handle.Value, queue);
			Require(ReferenceEquals(SynchronizationContext.Current, existingContext), "An existing context is kept.");
			Exception? fromQueue = null;
			sibling.UnhandledException += (_, e) =>
			{
				fromQueue = e.Exception;
				e.Handled = true;
			};
			queue.Post(_ => throw expected, null);
			Pump.Until(() => fromQueue is not null, TimeSpan.FromSeconds(5), "queue failure reported to the view");
			Require(ReferenceEquals(fromQueue, expected), "The default view reports the queue's failures.");
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

	// A host framework's dispatcher, here scheduling through the thread's queue. Can deny access to test the view's checks.
	private sealed class HostDispatcher(DirectNMessageQueue queue) : DirectNDispatcher
	{
		private int _enqueued;

		public bool AllowAccess { get; set; } = true;

		public int Enqueued => Volatile.Read(ref _enqueued);

		public override bool CheckAccess() => AllowAccess && queue.CheckAccess();

		protected override bool TryEnqueue(SendOrPostCallback callback, object? state, Action<object?> onAbandoned)
		{
			Interlocked.Increment(ref _enqueued);
			return queue.TryPost(callback, state, onAbandoned);
		}
	}
}
