using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using BlazorDispatcher = Microsoft.AspNetCore.Components.Dispatcher;
using UiDispatcher = Avalonia.Threading.Dispatcher;

namespace Avalonia.BlazorApp;

// One instance per view. Avalonia continues owning the dispatcher, context and message loop.
internal sealed class AvaloniaBlazorWebViewThreading : IBlazorWebViewThreading
{
	private readonly UiDispatcher _uiDispatcher;
	private bool _disposed;

	public AvaloniaBlazorWebViewThreading(UiDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		dispatcher.VerifyAccess();
		_uiDispatcher = dispatcher;
		SynchronizationContext = new AvaloniaSynchronizationContext(dispatcher, DispatcherPriority.Normal);
		Dispatcher = new AvaloniaBlazorDispatcher(dispatcher, ReportException);
	}

	public BlazorDispatcher Dispatcher { get; }

	public SynchronizationContext SynchronizationContext { get; }

	public bool CheckAccess() => Dispatcher.CheckAccess();

	public event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException;

	private void ReportException(Exception exception)
	{
		if (_disposed)
			return;

		var args = new BlazorWebViewUnhandledExceptionEventArgs(exception);
		try
		{
			UnhandledException?.Invoke(this, args);
			if (args.Handled)
				return;
		}
		catch (Exception handlerException)
		{
			exception = new AggregateException(exception, handlerException);
		}

		// Apply Avalonia's exception policy outside WebView2's native callback stack.
		var captured = ExceptionDispatchInfo.Capture(exception);
		_uiDispatcher.Post(captured.Throw);
	}

	public void Dispose()
	{
		_uiDispatcher.VerifyAccess();
		if (_disposed)
			return;

		_disposed = true;
		UnhandledException = null;
	}
}
