using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// <see cref="Dispatcher"/> implementation that invokes work on the UI thread through a <see cref="Win32MessageQueue"/>.
	/// </summary>
	internal sealed class Win32Dispatcher : Dispatcher
	{
		private readonly Win32MessageQueue _queue;
		private readonly Action<Exception> _onUnhandledException;

		/// <summary>
		/// Creates a new instance of <see cref="Win32Dispatcher"/>.
		/// </summary>
		/// <param name="queue">The queue bound to the UI thread.</param>
		/// <param name="onUnhandledException">Invoked on the UI thread with exceptions thrown by dispatched work. The
		/// owner ignores them once it has been disposed (mirroring WinForms, where a disposed control can't rethrow).</param>
		public Win32Dispatcher(Win32MessageQueue queue, Action<Exception> onUnhandledException)
		{
			_queue = queue ?? throw new ArgumentNullException(nameof(queue));
			_onUnhandledException = onUnhandledException ?? throw new ArgumentNullException(nameof(onUnhandledException));
		}

		public override bool CheckAccess() => _queue.CheckAccess();

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
					var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					_queue.Post(static state =>
					{
						var (workItem, tcs) = ((Action, TaskCompletionSource))state!;
						try
						{
							workItem();
							tcs.TrySetResult();
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					}, (workItem, tcs));
					await tcs.Task;
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
					var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					_queue.Post(static async state =>
					{
						var (workItem, tcs) = ((Func<Task>, TaskCompletionSource))state!;
						try
						{
							await workItem();
							tcs.TrySetResult();
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					}, (workItem, tcs));
					await tcs.Task;
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
				if (CheckAccess())
				{
					return workItem();
				}
				else
				{
					var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
					_queue.Post(static state =>
					{
						var (workItem, tcs) = ((Func<TResult>, TaskCompletionSource<TResult>))state!;
						try
						{
							tcs.TrySetResult(workItem());
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					}, (workItem, tcs));
					return await tcs.Task;
				}
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
				if (CheckAccess())
				{
					return await workItem();
				}
				else
				{
					var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
					_queue.Post(static async state =>
					{
						var (workItem, tcs) = ((Func<Task<TResult>>, TaskCompletionSource<TResult>))state!;
						try
						{
							tcs.TrySetResult(await workItem());
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					}, (workItem, tcs));
					return await tcs.Task;
				}
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

			try
			{
				_queue.Post(static state =>
				{
					var (self, exception) = ((Win32Dispatcher, Exception))state!;
					self._onUnhandledException(exception);
				}, (this, ex));
			}
			catch (System.ComponentModel.Win32Exception)
			{
				// The UI thread's message loop is gone; there is no one left to report to.
			}
		}

		private static bool IsRendererDisposedRace(Exception ex) =>
			ex is ArgumentException ae &&
			ae.Message.StartsWith("The renderer does not have a component with ID", StringComparison.Ordinal);
	}
}
