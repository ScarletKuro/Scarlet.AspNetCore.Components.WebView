namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Provides data for the <see cref="BlazorWebView.UnhandledException"/> event.
	/// </summary>
	public class BlazorWebViewUnhandledExceptionEventArgs : EventArgs
	{
		/// <summary>
		/// Creates event data for an exception reported by a threading integration.
		/// </summary>
		/// <param name="exception">The exception that was thrown.</param>
		public BlazorWebViewUnhandledExceptionEventArgs(Exception exception)
		{
			Exception = exception ?? throw new ArgumentNullException(nameof(exception));
		}

		/// <summary>
		/// Gets the exception that was thrown by work dispatched to the UI thread.
		/// </summary>
		public Exception Exception { get; }

		/// <summary>
		/// Gets or sets a value indicating whether the exception has been handled. When left <see langword="false"/>,
		/// the threading integration applies its host's unhandled-exception policy. The default DirectN integration
		/// rethrows it as an unhandled exception, terminating the process.
		/// </summary>
		public bool Handled { get; set; }
	}
}
