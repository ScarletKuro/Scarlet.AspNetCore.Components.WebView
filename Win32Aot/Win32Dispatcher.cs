using System;
using System.Threading.Tasks;
using Scarlet.DirectN.Threading;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// <see cref="Dispatcher"/> implementation that invokes work on the UI thread through a <see cref="DirectNDispatcher"/>.
	/// </summary>
	internal sealed class Win32Dispatcher : Dispatcher
	{
		private readonly DirectNDispatcher _dispatcher;
		private readonly Action<Exception> _onUnhandledException;

		/// <summary>
		/// Creates a new instance of <see cref="Win32Dispatcher"/>.
		/// </summary>
		/// <param name="dispatcher">The dispatcher bound to the UI thread.</param>
		/// <param name="onUnhandledException">Invoked on the UI thread with exceptions thrown by dispatched work. The
		/// owner ignores them once it has been disposed (mirroring WinForms, where a disposed control can't rethrow).</param>
		public Win32Dispatcher(DirectNDispatcher dispatcher, Action<Exception> onUnhandledException)
		{
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
			_onUnhandledException = onUnhandledException ?? throw new ArgumentNullException(nameof(onUnhandledException));
		}

		public override bool CheckAccess() => _dispatcher.CheckAccess();

		// On the UI thread, work runs inline, as Blazor expects. From other threads, DirectNDispatcher.InvokeAsync faults
		// the task with InvalidOperationException when the UI thread closes its queue or exits before the work runs.
		public override async Task InvokeAsync(Action workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);
			try
			{
				if (CheckAccess())
				{
					workItem();
				}
				else
				{
					await _dispatcher.InvokeAsync(workItem);
				}
			}
			catch (Exception ex)
			{
				// Surface unhandled exceptions on the UI thread so they're not silently lost.
				// The throw below still faults the returned Task for any awaiting caller.
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		public override async Task InvokeAsync(Func<Task> workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);
			try
			{
				if (CheckAccess())
				{
					await workItem();
				}
				else
				{
					await _dispatcher.InvokeAsync(workItem);
				}
			}
			catch (Exception ex)
			{
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		public override async Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);
			try
			{
				return CheckAccess() ? workItem() : await _dispatcher.InvokeAsync(workItem);
			}
			catch (Exception ex)
			{
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);
			try
			{
				return CheckAccess() ? await workItem() : await _dispatcher.InvokeAsync(workItem);
			}
			catch (Exception ex)
			{
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		// Fixes: https://github.com/dotnet/maui/issues/34855#issuecomment-4237810520
		// If you call DispatchExceptionAsync when WebView is closed, it throws that component doesn't exist.
		private void SafeRethrowOnDispatcher(Exception ex)
		{
			if (ex is ObjectDisposedException)
			{
				// Work raced with teardown of the view.
				return;
			}

			if (IsRendererDisposedRace(ex))
			{
				// Upstream aspnetcore bug: Renderer.HandleComponentException throws when the
				// target component has already been removed from the renderer (disposed /
				// navigated away) while a dispatched work item was still in flight. Promoting
				// it to the UI thread would crash the app via the unhandled exception path.
				// The exception still faults the returned Task so awaiting callers can observe it.
				return;
			}

			if (CheckAccess())
			{
				_onUnhandledException(ex);
				return;
			}

			// Dropped when the UI thread's queue is closed (also the reason work never ran): there is no one left to report to.
			_dispatcher.TryPost(() => _onUnhandledException(ex));
		}

		private static bool IsRendererDisposedRace(Exception ex) =>
			ex is ArgumentException ae &&
			ae.Message.StartsWith("The renderer does not have a component with ID", StringComparison.Ordinal);
	}
}
