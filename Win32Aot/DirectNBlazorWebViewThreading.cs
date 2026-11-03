using Scarlet.DirectN.Threading;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Default threading integration for a <see cref="BlazorWebView"/>, using the UI thread's shared
	/// <see cref="DirectNMessageQueue"/> and a view-specific Blazor dispatcher.
	/// </summary>
	public sealed class DirectNBlazorWebViewThreading : IBlazorWebViewThreading
	{
		private readonly DirectNMessageQueue _queue;
		private bool _disposed;

		/// <summary>
		/// Creates an integration on the current STA UI thread, which must run a Win32 message loop.
		/// </summary>
		public DirectNBlazorWebViewThreading()
		{
			if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
			{
				throw new InvalidOperationException("The WebView UI thread must be an STA thread.");
			}

			_queue = DirectNMessageQueue.GetOrCreateForCurrentThread();
			Dispatcher = new Win32Dispatcher(_queue, ReportDispatcherException);
			_queue.UnhandledException += HandleQueueUnhandledException;
		}

		/// <inheritdoc />
		public Dispatcher Dispatcher { get; }

		/// <inheritdoc />
		public SynchronizationContext SynchronizationContext => _queue.SynchronizationContext;

		/// <inheritdoc />
		public bool CheckAccess() => Dispatcher.CheckAccess();

		/// <inheritdoc />
		public event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException;

		private void ReportDispatcherException(Exception exception)
		{
			if (_disposed)
            {
                return;
            }

            var args = new BlazorWebViewUnhandledExceptionEventArgs(exception);
			try
			{
				UnhandledException?.Invoke(this, args);
			}
			catch (Exception handlerException)
			{
				DirectNMessageQueue.RethrowAsUnhandled(new AggregateException(exception, handlerException));
				return;
			}

			if (!args.Handled)
            {
                DirectNMessageQueue.RethrowAsUnhandled(exception);
            }
        }

		private void HandleQueueUnhandledException(object? sender, DirectNUnhandledExceptionEventArgs e)
		{
			if (!_disposed && !e.Handled)
			{
				var args = new BlazorWebViewUnhandledExceptionEventArgs(e.Exception);
				UnhandledException?.Invoke(this, args);
				e.Handled = args.Handled;
			}
		}

		/// <summary>
		/// Detaches this view's exception callbacks on the UI thread. The shared queue and its context stay alive;
		/// already dispatched work and other views can continue using them.
		/// </summary>
		public void Dispose()
		{
			if (!CheckAccess())
            {
                throw new InvalidOperationException("The threading integration must be disposed on its UI thread.");
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;
			_queue.UnhandledException -= HandleQueueUnhandledException;
			UnhandledException = null;
		}
	}
}
