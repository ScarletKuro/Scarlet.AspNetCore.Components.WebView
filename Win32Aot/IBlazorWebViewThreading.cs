using System;
using System.Threading;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Provides the UI-thread dispatcher, synchronization context and exception reporting for one
	/// <see cref="BlazorWebView"/>.
	/// </summary>
	/// <remarks>
	/// Create an implementation on the parent window's STA UI thread. Its dispatcher and synchronization context
	/// must target that thread and remain usable until view disposal finishes. A view takes ownership of the
	/// implementation after successful construction; do not share the implementation between views.
	/// <see cref="IDisposable.Dispose"/> must detach this view's callbacks without stopping a shared dispatcher,
	/// queue or message loop. It must be idempotent, and dispatched work must still be able to complete afterward.
	/// </remarks>
	public interface IBlazorWebViewThreading : IDisposable
	{
		/// <summary>
		/// Gets the dispatcher used by the Blazor renderer. It must remain the same for this implementation's lifetime.
		/// </summary>
		Dispatcher Dispatcher { get; }

		/// <summary>
		/// Gets a context that resumes asynchronous work on the owning UI thread. The view installs it only when
		/// the current thread has no synchronization context. Dispatched callbacks must also use a suitable UI context.
		/// </summary>
		SynchronizationContext SynchronizationContext { get; }

		/// <summary>
		/// Returns whether the calling thread is the owning UI thread, consistently with <see cref="Dispatcher"/>.
		/// </summary>
		bool CheckAccess();

		/// <summary>
		/// Raised on the UI thread when dispatched work fails, including fire-and-forget work. Awaiting callers must
		/// still receive the failure. The implementation applies the host's unhandled-exception policy when no handler
		/// sets <see cref="BlazorWebViewUnhandledExceptionEventArgs.Handled"/>, or when a handler itself throws.
		/// </summary>
		event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException;
	}
}
