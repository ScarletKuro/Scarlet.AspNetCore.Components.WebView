// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Microsoft.AspNetCore.Components.WebView.WindowsForms
{
	/// <summary>
	/// Dispatcher implementation for Windows Forms that invokes methods on the UI thread. The <see cref="Dispatcher"/>
	/// class uses the async <see cref="Task"/> pattern so everything must be mapped from the <see cref="IAsyncResult"/>
	/// pattern using techniques listed in https://docs.microsoft.com/dotnet/standard/asynchronous-programming-patterns/interop-with-other-asynchronous-patterns-and-types.
	/// </summary>
	internal sealed class WindowsFormsDispatcher : Dispatcher
	{
		private static Action<Exception> RethrowException = exception =>
			ExceptionDispatchInfo.Capture(exception).Throw();
		private readonly Control _dispatchThreadControl;

		/// <summary>
		/// Creates a new instance of <see cref="WindowsFormsDispatcher"/>.
		/// </summary>
		/// <param name="dispatchThreadControl">A control that was created on the thread from which UI dispatches must
		/// occur. This can typically be any control because all controls must have been created on the UI thread to
		/// begin with.</param>
		public WindowsFormsDispatcher(Control dispatchThreadControl)
		{
			if (dispatchThreadControl is null)
			{
				throw new ArgumentNullException(nameof(dispatchThreadControl));
			}

			_dispatchThreadControl = dispatchThreadControl;
		}

		public override bool CheckAccess()
			=> !_dispatchThreadControl.InvokeRequired;

		public override async Task InvokeAsync(Action workItem)
		{
			try
			{
				if (CheckAccess())
				{
					workItem();
				}
				else
				{
					var asyncResult = _dispatchThreadControl.BeginInvoke(workItem);
					await Task.Factory.FromAsync(asyncResult, _dispatchThreadControl.EndInvoke);
				}
			}
			catch (Exception ex)
			{
				// Surface unhandled exceptions on the UI thread so they're not silently lost,
				// but skip that promotion when the host control is disposed/disposing or when
				// the exception is the known disposed-renderer race (see SafeRethrowOnDispatcher).
				// The throw below still faults the returned Task for any awaiting caller.
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		public override async Task InvokeAsync(Func<Task> workItem)
		{
			try
			{
				if (CheckAccess())
				{
					await workItem();
				}
				else
				{
					// See https://github.com/dotnet/winforms/issues/4631 for discussion. `Control.BeginInvoke` in WinForms
					// does not wait for Tasks returned by the delegate. We will have to simulate this using a TCS and wait for
					// both execution of `workItem` and the dispatcher to complete its internal operation.
					// additional APIs are exposed by WinForms.

					var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					// BeginInvoke specifically expects an `Action` so avoid using var here.
					Action action = async () =>
					{
						try
						{
							await workItem();
							tcs.TrySetResult();
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					};

					var asyncResult = _dispatchThreadControl.BeginInvoke(action, workItem, tcs);
					await Task.WhenAll(tcs.Task, Task.Factory.FromAsync(asyncResult, _dispatchThreadControl.EndInvoke));
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
			try
			{
				if (CheckAccess())
				{
					return workItem();
				}
				else
				{
					var asyncResult = _dispatchThreadControl.BeginInvoke(workItem);
					return await Task<TResult>.Factory.FromAsync(asyncResult, result => (TResult)_dispatchThreadControl.EndInvoke(result)!);
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
			try
			{
				if (CheckAccess())
				{
					return await workItem();
				}
				else
				{
					// See https://github.com/dotnet/winforms/issues/4631 for discussion. `Control.BeginInvoke` in WinForms
					// does not wait for Tasks returned by the delegate. We will have to simulate this using a TCS and wait for
					// both execution of `workItem` and the dispatcher to complete its internal operation.
					// additional APIs are exposed by WinForms.

					var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
					// BeginInvoke specifically expects an `Action` so avoid using var here.
					Action action = async () =>
					{
						try
						{
							var result = await workItem();
							tcs.TrySetResult(result);
						}
						catch (Exception ex)
						{
							tcs.TrySetException(ex);
						}
					};

					var asyncResult = _dispatchThreadControl.BeginInvoke(action, workItem, tcs);
					await Task.WhenAll(tcs.Task, Task.Factory.FromAsync(asyncResult, _dispatchThreadControl.EndInvoke));
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
			if (_dispatchThreadControl.IsDisposed ||
				_dispatchThreadControl.Disposing ||
				!_dispatchThreadControl.IsHandleCreated)
			{
				// No surviving UI thread to receive the rethrow.
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

			try
			{
				_ = _dispatchThreadControl.BeginInvoke(RethrowException, ex);
			}
			catch (ObjectDisposedException)
			{
				// Race: the control was disposed between the check and BeginInvoke.
			}
			catch (InvalidOperationException)
			{
				// Race: the control's handle was destroyed between the check and BeginInvoke.
			}
		}

		private static bool IsRendererDisposedRace(Exception ex) =>
			ex is ArgumentException ae &&
			ae.Message.StartsWith("The renderer does not have a component with ID", StringComparison.Ordinal);
	}
}
