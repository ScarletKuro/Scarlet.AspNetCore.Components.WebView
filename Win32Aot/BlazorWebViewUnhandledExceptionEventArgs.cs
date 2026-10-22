using System;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Provides data for the <see cref="BlazorWebView.UnhandledException"/> event.
	/// </summary>
	public class BlazorWebViewUnhandledExceptionEventArgs : EventArgs
	{
		internal BlazorWebViewUnhandledExceptionEventArgs(Exception exception)
		{
			Exception = exception;
		}

		/// <summary>
		/// Gets the exception that was thrown by work dispatched to the UI thread.
		/// </summary>
		public Exception Exception { get; }

		/// <summary>
		/// Gets or sets a value indicating whether the exception has been handled. When left <see langword="false"/>,
		/// the exception is rethrown as an unhandled exception, which terminates the process
		/// (matching the default behavior of WinForms and WPF).
		/// </summary>
		public bool Handled { get; set; }
	}
}
