using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Controls;
using RECT = DirectN.RECT;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;

namespace Avalonia.BlazorApp;

internal sealed class BlazorViewHost(IServiceProvider services, Action<BlazorWebView> configureComponents) : NativeControlHost, IAsyncDisposable
{
	private NativeWindowMessages? _nativeWindow;
	private BlazorWebView? _view;
	private Task? _disposeTask;
	private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public event Action<string>? StatusChanged;
	public Task Ready => _ready.Task;
	public BlazorWebView View => _view ?? throw new InvalidOperationException("The native host is not created yet.");
	public RECT NativeClientRect => _nativeWindow?.ClientRect ?? default;

	protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
	{
		Dispatcher.VerifyAccess();
		ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
		var child = base.CreateNativeControlCore(parent);
		try
		{
			_nativeWindow = new NativeWindowMessages(child.Handle, HandleWindowMessage,
				ex => Dispatcher.Post(() => ReportException(ex)));
			_view = new BlazorWebView(child.Handle, new AvaloniaUiDispatcher(Dispatcher))
			{
				HostPage = @"wwwroot\index.html",
				Services = services,
				Bounds = _nativeWindow.ClientRect,
			};
			_view.UnhandledException += HandleUnhandledException;
			configureComponents(_view);
			_view.UrlLoading += (_, e) =>
			{
				if (e.Url.IsFile)
					e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
			};
			_ = StartAsync(_view);
			return child;
		}
		catch
		{
			_nativeWindow?.Dispose();
			_nativeWindow = null;
			base.DestroyNativeControlCore(child);
			throw;
		}
	}

	private async Task StartAsync(BlazorWebView view)
	{
		try
		{
			await view.StartAsync();
			if (!view.IsDisposed)
			{
				SynchronizeBounds();
				StatusChanged?.Invoke("WebView2 ready; Blazor uses Avalonia's dispatcher.");
				_ready.TrySetResult();
			}
			else
			{
				_ready.TrySetCanceled();
			}
		}
		catch (Exception ex)
		{
			_ready.TrySetException(ex);
			ReportException(ex);
		}
	}

	private void HandleWindowMessage(uint message)
	{
		if (_view is not { IsDisposed: false } view)
		{
			return;
		}

		switch (message)
		{
			case NativeWindowMessages.Size:
				SynchronizeBounds();
				break;
			case NativeWindowMessages.WindowPositionChanged:
				view.NotifyParentWindowPositionChanged();
				break;
			case NativeWindowMessages.SetFocus:
				view.MoveFocus();
				break;
		}
	}

	private void SynchronizeBounds()
	{
		if (_view is { IsDisposed: false } view && _nativeWindow is { } native)
		{
			view.Bounds = native.ClientRect;
		}
	}

	public void NotifyWindowMoved() => _view?.NotifyParentWindowPositionChanged();

	public void SetWindowMinimized(bool minimized)
	{
		if (_view is { IsDisposed: false } view)
		{
			view.IsVisible = !minimized && IsEffectivelyVisible;
		}
	}

	protected override void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);
		if (_view is { IsDisposed: false } view)
			view.MoveFocus();
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == IsVisibleProperty && _view is { IsDisposed: false } view)
		{
			view.IsVisible = IsEffectivelyVisible;
		}
	}

	// NativeControlHost's destruction callback is synchronous. Explicit disposal is awaited by MainWindow
	// before normal close; detaching the control also keeps its child HWND alive until disposal finishes.
	protected override void DestroyNativeControlCore(IPlatformHandle control) => _ = DestroyAsync(control);

	private async Task DestroyAsync(IPlatformHandle control)
	{
		var native = _nativeWindow;
		_nativeWindow = null;
		try
		{
			await DisposeAsync();
		}
		catch (Exception ex)
		{
			ReportException(ex);
		}
		finally
		{
			native?.Dispose();
			base.DestroyNativeControlCore(control);
		}
	}

	public ValueTask DisposeAsync()
	{
		Dispatcher.VerifyAccess();
		return new ValueTask(_disposeTask ??= DisposeCoreAsync());
	}

	private async Task DisposeCoreAsync()
	{
		_ready.TrySetCanceled();
		if (_view is { } view)
			await view.DisposeAsync();
	}

	private void ReportException(Exception exception)
	{
		System.Diagnostics.Trace.TraceError(exception.ToString());
		StatusChanged?.Invoke($"Error: {exception.Message}");
	}

	private void HandleUnhandledException(object? sender, BlazorWebViewUnhandledExceptionEventArgs e)
	{
		// This sample shows errors in the Avalonia status area and lets the application continue.
		ReportException(e.Exception);
		e.Handled = true;
	}
}
