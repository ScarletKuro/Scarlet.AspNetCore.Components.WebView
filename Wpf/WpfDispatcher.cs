// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using WindowsDispatcher = System.Windows.Threading.Dispatcher;

namespace Microsoft.AspNetCore.Components.WebView.Wpf
{
	internal sealed class WpfDispatcher : Dispatcher
	{
		private readonly WindowsDispatcher _windowsDispatcher;

		public WpfDispatcher(WindowsDispatcher windowsDispatcher)
		{
			_windowsDispatcher = windowsDispatcher ?? throw new ArgumentNullException(nameof(windowsDispatcher));
		}

		private static Action<Exception> RethrowException = exception =>
			ExceptionDispatchInfo.Capture(exception).Throw();

		public override bool CheckAccess()
			=> _windowsDispatcher.CheckAccess();

		public override async Task InvokeAsync(Action workItem)
		{
			try
			{
				if (_windowsDispatcher.CheckAccess())
				{
					workItem();
				}
				else
				{
					await _windowsDispatcher.InvokeAsync(workItem);
				}
			}
			catch (Exception ex)
			{
				// Surface unhandled exceptions on the UI thread so they're not silently lost,
				// but skip that promotion when the UI dispatcher is shutting down or when the
				// exception is the known disposed-renderer race (see SafeRethrowOnDispatcher).
				// The throw below still faults the returned Task for any awaiting caller.
				SafeRethrowOnDispatcher(ex);
				throw;
			}
		}

		public override async Task InvokeAsync(Func<Task> workItem)
		{
			try
			{
				if (_windowsDispatcher.CheckAccess())
				{
					await workItem();
				}
				else
				{
					await _windowsDispatcher.InvokeAsync(workItem);
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
				if (_windowsDispatcher.CheckAccess())
				{
					return workItem();
				}
				else
				{
					return await _windowsDispatcher.InvokeAsync(workItem);
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
				if (_windowsDispatcher.CheckAccess())
				{
					return await workItem();
				}
				else
				{
					return await _windowsDispatcher.InvokeAsync(workItem).Task.Unwrap();
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
			if (_windowsDispatcher.HasShutdownStarted || _windowsDispatcher.HasShutdownFinished)
			{
				// No surviving UI thread to receive the rethrow.
				return;
			}

			if (IsRendererDisposedRace(ex))
			{
				// Upstream aspnetcore bug: Renderer.HandleComponentException throws when the
				// target component has already been removed from the renderer (disposed /
				// navigated away) while a dispatched work item was still in flight. Promoting
				// it to the UI thread would crash the app via Dispatcher.UnhandledException.
				// The exception still faults the returned Task so awaiting callers can observe it.
				return;
			}

			_ = _windowsDispatcher.BeginInvoke(RethrowException, ex);
		}

		private static bool IsRendererDisposedRace(Exception ex) =>
			ex is ArgumentException ae &&
			ae.Message.StartsWith("The renderer does not have a component with ID", StringComparison.Ordinal);
	}
}
