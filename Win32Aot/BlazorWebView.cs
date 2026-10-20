// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HWND = DirectN.HWND;
using RECT = DirectN.RECT;
using DirectN.Extensions.Com;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebView2;
using WebView2.Utilities;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Hosts Razor components in a WebView2 attached to any Win32 window, without depending on a UI framework.
	/// The parent can be a DirectN <c>Window</c> (see <see cref="BlazorWebViewWindow"/>), a WinForms/WPF window handle,
	/// or a hand-written Win32 window.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The instance must be created, used and disposed on the parent window's UI thread, which must be an STA thread
	/// running a Win32 message loop.
	/// </para>
	/// <para>
	/// The view does not track the parent's size: forward size changes to <see cref="Bounds"/>, moves to
	/// <see cref="NotifyParentWindowPositionChanged"/> and focus to <see cref="MoveFocus"/>.
	/// <see cref="BlazorWebViewWindow"/> does all of this for you.
	/// </para>
	/// </remarks>
	public class BlazorWebView : IAsyncDisposable
	{
		private readonly nint _parentWindow;
		private readonly Win32MessageQueue _queue;
		private readonly Win32Dispatcher _dispatcher;
		private Win32AotWebViewManager? _webviewManager;
		private ILogger _logger = NullLogger<BlazorWebView>.Instance;
		private string? _hostPage;
		private string _startPath = "/";
		private IServiceProvider? _services;
		private RECT? _bounds;
		private bool _isVisible = true;
		private Task? _startTask;
		private Task? _disposeTask;

		/// <summary>
		/// Creates a new instance of <see cref="BlazorWebView"/> hosted in <paramref name="parentWindow"/>.
		/// Must be called on the parent window's UI thread.
		/// </summary>
		/// <param name="parentWindow">The handle (HWND) of the window that will host the WebView2.</param>
		public BlazorWebView(nint parentWindow)
		{
			if (parentWindow == 0)
			{
				throw new ArgumentException("A valid parent window handle is required.", nameof(parentWindow));
			}

			if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
			{
				throw new InvalidOperationException("BlazorWebView must be created on an STA thread. Mark the entry point with [STAThread].");
			}

			_parentWindow = parentWindow;
			_queue = Win32MessageQueue.GetOrCreateForCurrentThread();
			_queue.UnhandledException += HandleQueueUnhandledException;
			_dispatcher = new Win32Dispatcher(_queue, HandleUnhandledException);

			// await continuations must come back to the UI thread because WebView2 objects are thread-affine.
			// Host frameworks such as WinForms or WPF already provide a suitable context; keep theirs.
			if (SynchronizationContext.Current is null)
			{
				SynchronizationContext.SetSynchronizationContext(_queue.SynchronizationContext);
			}

			RootComponents.CollectionChanged += HandleRootComponentsCollectionChanged;
		}

		/// <summary>
		/// Gets the handle of the window hosting the WebView2.
		/// </summary>
		public nint ParentWindow => _parentWindow;

		/// <summary>
		/// Gets the <see cref="Components.Dispatcher"/> that runs work on this view's UI thread.
		/// </summary>
		public Dispatcher Dispatcher => _dispatcher;

		/// <summary>
		/// Path to the host page within the application's static files. For example, <code>wwwroot\index.html</code>.
		/// This property must be set before calling <see cref="StartAsync"/>.
		/// </summary>
		public string? HostPage
		{
			get => _hostPage;
			set
			{
				ThrowIfStarted();
				_hostPage = value;
			}
		}

		/// <summary>
		/// Path for initial Blazor navigation when the Blazor component is finished loading. Defaults to <c>/</c>.
		/// </summary>
		public string StartPath
		{
			get => _startPath;
			set
			{
				ArgumentNullException.ThrowIfNull(value);
				ThrowIfStarted();
				_startPath = value;
			}
		}

		/// <summary>
		/// A collection of <see cref="RootComponent"/> instances that specify the Blazor <see cref="IComponent"/> types
		/// to be used directly in the specified <see cref="HostPage"/>. May be changed at any time.
		/// </summary>
		public RootComponentsCollection RootComponents { get; } = new();

		/// <summary>
		/// Gets or sets an <see cref="IServiceProvider"/> containing services to be used by this view and also by application code.
		/// This property must be set before calling <see cref="StartAsync"/>.
		/// </summary>
		public IServiceProvider? Services
		{
			get => _services;
			set
			{
				ThrowIfStarted();
				_services = value;
			}
		}

		/// <summary>
		/// Gets the WebView2 controller, or <see langword="null"/> before initialization completes or after disposal.
		/// </summary>
		/// <remarks>
		/// Directly using some functionality of the controller can cause unexpected results because its behavior
		/// is controlled by the <see cref="BlazorWebView"/>. Do not close or dispose it.
		/// </remarks>
		public IComObject<ICoreWebView2Controller>? Controller => _webviewManager?.Controller;

		/// <summary>
		/// Gets the <see cref="ICoreWebView2"/>, or <see langword="null"/> before initialization completes or after disposal.
		/// </summary>
		/// <remarks>
		/// Directly using some functionality of the web view can cause unexpected results because its behavior
		/// is controlled by the <see cref="BlazorWebView"/>. Do not dispose it.
		/// </remarks>
		public IComObject<ICoreWebView2>? WebView => _webviewManager?.WebView;

		/// <summary>
		/// Gets or sets the bounds of the WebView2 relative to the parent window's client area.
		/// Values set before initialization are applied once the controller is created.
		/// </summary>
		public RECT Bounds
		{
			get => Controller?.Bounds ?? _bounds ?? default;
			set
			{
				_bounds = value;
				if (Controller is { } controller)
				{
					controller.Bounds = value;
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether the WebView2 is visible. Defaults to <see langword="true"/>.
		/// </summary>
		public bool IsVisible
		{
			get => _isVisible;
			set
			{
				_isVisible = value;
				if (Controller is { } controller)
				{
					controller.IsVisible = value;
				}
			}
		}

		/// <summary>
		/// Gets a value indicating whether this view has been disposed (or disposal has started).
		/// </summary>
		public bool IsDisposed => _disposeTask is not null;

		/// <summary>
		/// Allows customizing how links are opened.
		/// By default, opens internal links in the webview and external links in an external app.
		/// </summary>
		public event EventHandler<UrlLoadingEventArgs>? UrlLoading;

		/// <summary>
		/// Allows customizing the web view before it is created.
		/// </summary>
		public event EventHandler<BlazorWebViewInitializingEventArgs>? BlazorWebViewInitializing;

		/// <summary>
		/// Allows customizing the web view after it is created.
		/// </summary>
		public event EventHandler<BlazorWebViewInitializedEventArgs>? BlazorWebViewInitialized;

		/// <summary>
		/// Raised on the UI thread when work dispatched by Blazor throws. Set
		/// <see cref="BlazorWebViewUnhandledExceptionEventArgs.Handled"/> to keep the process alive; otherwise
		/// the exception is rethrown as an unhandled exception.
		/// </summary>
		public event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException;

		/// <summary>
		/// Starts WebView2 and Blazor. Completes once WebView2 is initialized and the host page navigation has been issued.
		/// Calling it more than once returns the same task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Required properties are missing.</exception>
		/// <exception cref="Exception">WebView2 could not be started, e.g. because the WebView2 Runtime is not installed.</exception>
		public Task StartAsync()
		{
			ObjectDisposedException.ThrowIf(IsDisposed, this);
			ThrowIfNotOnUIThread();

			return _startTask ??= StartCoreAsync();
		}

		private async Task StartCoreAsync()
		{
			if (HostPage is null)
			{
				throw new InvalidOperationException($"'{nameof(HostPage)}' must be set before calling '{nameof(StartAsync)}'.");
			}

			if (Services is null)
			{
				throw new InvalidOperationException($"'{nameof(Services)}' must be set before calling '{nameof(StartAsync)}'.");
			}

			_logger = Services.GetService<ILogger<BlazorWebView>>() ?? NullLogger<BlazorWebView>.Instance;
			var logger = _logger;

			// We assume the host page is always in the root of the content directory, because it's
			// unclear there's any other use case. We can add more options later if so.
			// AppContext.BaseDirectory is the application directory for both JIT and NativeAOT/single-file apps.
			var appRootDir = AppContext.BaseDirectory;
			var hostPageFullPath = Path.GetFullPath(Path.Combine(appRootDir, HostPage));
			var contentRootDirFullPath = Path.GetDirectoryName(hostPageFullPath)!;
			var contentRootRelativePath = Path.GetRelativePath(appRootDir, contentRootDirFullPath);
			var hostPageRelativePath = Path.GetRelativePath(contentRootDirFullPath, hostPageFullPath);

			logger.CreatingFileProvider(contentRootDirFullPath, hostPageRelativePath);
			var fileProvider = CreateFileProvider(contentRootDirFullPath);

			var webviewManager = new Win32AotWebViewManager(
				_parentWindow,
				Services,
				_dispatcher,
				fileProvider,
				RootComponents.JSComponents,
				contentRootRelativePath,
				hostPageRelativePath,
				(args) => UrlLoading?.Invoke(this, args),
				(args) => BlazorWebViewInitializing?.Invoke(this, args),
				OnWebViewInitialized,
				logger);
			_webviewManager = webviewManager;

			StaticContentHotReloadManager.AttachToWebViewManagerIfEnabled(webviewManager);

			foreach (var rootComponent in RootComponents)
			{
				logger.AddingRootComponent(rootComponent.ComponentType.FullName ?? string.Empty, rootComponent.Selector, rootComponent.Parameters?.Count ?? 0);

				// Since the page isn't loaded yet, this will always complete synchronously
				_ = rootComponent.AddToWebViewManagerAsync(webviewManager);
			}

			logger.StartingInitialNavigation(StartPath);
			webviewManager.Navigate(StartPath);

			await webviewManager.WebViewReadyTask.ConfigureAwait(true);
		}

		private void OnWebViewInitialized(BlazorWebViewInitializedEventArgs args)
		{
			var controller = args.Controller;
			controller.Bounds = _bounds ?? GetParentClientRect();
			controller.IsVisible = _isVisible;

			BlazorWebViewInitialized?.Invoke(this, args);
		}

		private RECT GetParentClientRect()
		{
			DirectN.Functions.GetClientRect(new HWND { Value = _parentWindow }, out var rect);
			return rect;
		}

		/// <summary>
		/// Notifies WebView2 that the parent window moved, so that dropdowns and other popups are positioned correctly.
		/// Call it when the parent window receives <c>WM_MOVE</c>/<c>WM_MOVING</c>.
		/// </summary>
		public void NotifyParentWindowPositionChanged()
		{
			Controller?.NotifyParentWindowPositionChanged(throwOnError: false);
		}

		/// <summary>
		/// Moves keyboard focus into the WebView2. Call it when the parent window receives focus.
		/// </summary>
		/// <param name="reason">The reason focus is moving.</param>
		/// <returns><see langword="true"/> if the WebView2 is initialized and took the focus.</returns>
		public bool MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON reason = COREWEBVIEW2_MOVE_FOCUS_REASON.COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC)
		{
			return Controller is { } controller && controller.MoveFocus(reason, throwOnError: false).IsSuccess;
		}

		private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
		{
			// If we haven't initialized yet, this is a no-op
			if (_webviewManager is { } webviewManager && !IsDisposed)
			{
				// Dispatch because this is going to be async, and we want to catch any errors
				_ = _dispatcher.InvokeAsync(async () =>
				{
					var newItems = (eventArgs.NewItems ?? Array.Empty<object>()).Cast<RootComponent>();
					var oldItems = (eventArgs.OldItems ?? Array.Empty<object>()).Cast<RootComponent>();

					foreach (var item in newItems.Except(oldItems))
					{
						await item.AddToWebViewManagerAsync(webviewManager);
					}

					foreach (var item in oldItems.Except(newItems))
					{
						await item.RemoveFromWebViewManagerAsync(webviewManager);
					}
				});
			}
		}

		/// <summary>
		/// Creates a file provider for static assets used in the <see cref="BlazorWebView"/>. The default implementation
		/// serves files from disk. Override this method to return a custom <see cref="IFileProvider"/> to serve assets such
		/// as <c>wwwroot/index.html</c>. Call the base method and combine its return value with a <see cref="CompositeFileProvider"/>
		/// to use both custom assets and default assets.
		/// </summary>
		/// <param name="contentRootDir">The base directory to use for all requested assets, such as <c>wwwroot</c>.</param>
		/// <returns>Returns a <see cref="IFileProvider"/> for static assets.</returns>
		public virtual IFileProvider CreateFileProvider(string contentRootDir)
		{
			if (Directory.Exists(contentRootDir))
			{
				// Typical case after publishing, or if you're copying content to the bin dir in development for some nonstandard reason
				return new PhysicalFileProvider(contentRootDir);
			}
			else
			{
				// Typical case in development, as the files come from Microsoft.AspNetCore.Components.WebView.StaticContentProvider
				// instead and aren't copied to the bin dir
				return new NullFileProvider();
			}
		}

		/// <summary>
		/// Calls the specified <paramref name="workItem"/> asynchronously and passes in the scoped services available to Razor components.
		/// </summary>
		/// <param name="workItem">The action to call.</param>
		/// <returns>Returns a <see cref="Task"/> representing <c>true</c> if the <paramref name="workItem"/> was called, or <c>false</c> if it was not called because Blazor is not currently running.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="workItem"/> is <c>null</c>.</exception>
		public virtual async Task<bool> TryDispatchAsync(Action<IServiceProvider> workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);
			if (_webviewManager is null || IsDisposed)
			{
				return false;
			}

			return await _webviewManager.TryDispatchAsync(workItem);
		}

		/// <summary>
		/// Disconnects the IPC bridge between the host application and the WebView.
		/// </summary>
		/// <remarks>
		/// This method overrides the default IPC behavior by replacing the
		/// <c>window.external</c> object in the WebView with no-op implementations.
		/// This prevents further message passing between JavaScript and the host.
		///
		/// This is a workaround for a known issue:
		/// https://github.com/dotnet/maui/issues/34855
		/// </remarks>
		protected virtual async Task DisconnectIpcAsync()
		{
			// Fixes: https://github.com/dotnet/maui/issues/34855
			if (WebView is { } webView)
			{
				await webView.ExecuteScriptAsync(
					"""
					window.external = {
					  sendMessage: message => {},
					  receiveMessage: callback => {}
					};
					""");
			}
		}

		/// <summary>
		/// Disposes Razor components and tears down WebView2. Must be called on the UI thread; subsequent calls return
		/// the same task.
		/// </summary>
		/// <remarks>
		/// Await this before destroying the parent window: component disposal logic needs the WebView2 to still exist.
		/// </remarks>
		public ValueTask DisposeAsync()
		{
			if (_disposeTask is null)
			{
				ThrowIfNotOnUIThread();
				_disposeTask = DisposeCoreAsync();
			}

			return new ValueTask(_disposeTask);
		}

		/// <summary>
		/// Releases resources. Override to add custom teardown and call the base implementation.
		/// </summary>
		protected virtual async Task DisposeCoreAsync()
		{
			RootComponents.CollectionChanged -= HandleRootComponentsCollectionChanged;

			try
			{
				var webviewManager = _webviewManager;
				if (webviewManager is not null)
				{
					try
					{
						await DisconnectIpcAsync();
					}
					catch (Exception ex)
					{
						// Best effort: WebView2 may already be gone (e.g. the browser process crashed).
						_logger.LogDebug(ex, "Failed to disconnect the WebView2 IPC bridge.");
					}

					// Await disposal of this view's contents so that user-written disposal logic and
					// Razor component disposal logic will complete first. Then release WebView2, since
					// once it's gone Razor component code can no longer work.
					await _dispatcher.InvokeAsync(async () => await webviewManager.DisposeAsync());
					_webviewManager = null;
				}
			}
			finally
			{
				// The UI thread's queue and synchronization context deliberately stay alive: continuations of code
				// awaiting this disposal (and other views on the thread) still need them.
				_queue.UnhandledException -= HandleQueueUnhandledException;
			}
		}

		// Exceptions from Blazor dispatcher work items.
		private void HandleUnhandledException(Exception exception)
		{
			if (IsDisposed)
			{
				// Mirrors WinForms: once the view is gone there is nothing to promote the exception to.
				// The exception still faults the Task returned to whoever awaited the work.
				_logger.LogDebug(exception, "Ignoring an exception from work dispatched after the BlazorWebView was disposed.");
				return;
			}

			var args = new BlazorWebViewUnhandledExceptionEventArgs(exception);
			try
			{
				RaiseUnhandledException(args);
			}
			catch (Exception handlerException)
			{
				Win32MessageQueue.RethrowAsUnhandled(new AggregateException(exception, handlerException));
				return;
			}

			if (!args.Handled)
			{
				Win32MessageQueue.RethrowAsUnhandled(exception);
			}
		}

		// Exceptions from other code posted to the UI thread's synchronization context (e.g. async void continuations).
		private void HandleQueueUnhandledException(object? sender, BlazorWebViewUnhandledExceptionEventArgs args)
		{
			if (!args.Handled)
			{
				RaiseUnhandledException(args);
			}
		}

		private void RaiseUnhandledException(BlazorWebViewUnhandledExceptionEventArgs args)
		{
			_logger.LogError(args.Exception, "Unhandled exception in work dispatched by BlazorWebView.");
			UnhandledException?.Invoke(this, args);
		}

		private void ThrowIfStarted()
		{
			if (_startTask is not null)
			{
				throw new InvalidOperationException("This property can't be changed after the BlazorWebView has started.");
			}
		}

		private void ThrowIfNotOnUIThread()
		{
			if (!_queue.CheckAccess())
			{
				throw new InvalidOperationException("BlazorWebView must be used on the thread that created it.");
			}
		}
	}
}
