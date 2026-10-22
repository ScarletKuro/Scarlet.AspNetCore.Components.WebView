using System;
using System.ComponentModel;
using System.Threading.Tasks;
using DirectN.Extensions.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RECT = DirectN.RECT;
using SIZE = DirectN.SIZE;
using WINDOW_EX_STYLE = DirectN.WINDOW_EX_STYLE;
using WINDOW_STYLE = DirectN.WINDOW_STYLE;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// A top-level DirectN <see cref="Window"/> whose client area is filled by a <see cref="Win32Aot.BlazorWebView"/>.
	/// It forwards size, move and focus changes to the view, starts it once the window is created, and disposes it
	/// before the window is destroyed.
	/// </summary>
	/// <example>
	/// <code>
	/// [STAThread]
	/// static void Main()
	/// {
	///     using var services = new ServiceCollection().AddWin32AotBlazorWebView().Services.BuildServiceProvider();
	///     using var app = new Application();
	///     using var window = new BlazorWebViewWindow("My app");
	///     window.BlazorWebView.HostPage = @"wwwroot\index.html";
	///     window.BlazorWebView.Services = services;
	///     window.BlazorWebView.RootComponents.Add&lt;App&gt;("#app");
	///     window.Show();
	///     app.Run();
	/// }
	/// </code>
	/// </example>
	public class BlazorWebViewWindow : Window
	{
		private readonly BlazorWebView? _blazorWebView;
		private bool _closeAfterDispose;
		private bool _isClosing;

		/// <summary>
		/// Creates the window and its <see cref="Win32Aot.BlazorWebView"/>. Must be called on an STA thread that runs
		/// a DirectN <see cref="Application"/> message loop.
		/// </summary>
		/// <param name="title">The window title.</param>
		/// <param name="style">The window style.</param>
		/// <param name="extendedStyle">The extended window style.</param>
		/// <param name="rect">The initial window rectangle, or <see langword="null"/> for the system default.</param>
		public BlazorWebViewWindow(
			string? title = null,
			WINDOW_STYLE style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
			WINDOW_EX_STYLE extendedStyle = 0,
			RECT? rect = null)
			: base(title, style, extendedStyle, rect)
		{
			_blazorWebView = CreateBlazorWebView(Handle.Value);
		}

		/// <summary>
		/// Gets the <see cref="Win32Aot.BlazorWebView"/> hosted by this window. Configure
		/// <see cref="BlazorWebView.HostPage"/>, <see cref="BlazorWebView.Services"/> and
		/// <see cref="BlazorWebView.RootComponents"/> right after construction.
		/// </summary>
		public BlazorWebView BlazorWebView => _blazorWebView ?? throw new InvalidOperationException("The BlazorWebView is not created yet.");

		/// <summary>
		/// Gets or sets a value indicating whether the <see cref="BlazorWebView"/> is started automatically once the window
		/// has been created (the first time the message loop runs). Defaults to <see langword="true"/>. When
		/// <see langword="false"/>, call <see cref="BlazorWebView.StartAsync"/> yourself.
		/// </summary>
		public bool AutoStart { get; set; } = true;

		/// <summary>
		/// Creates the hosted view. Override to return a <see cref="Win32Aot.BlazorWebView"/> subclass, e.g. one that
		/// overrides <see cref="BlazorWebView.CreateFileProvider(string)"/>. Called from the constructor.
		/// </summary>
		/// <param name="parentWindow">The handle of this window.</param>
		protected virtual BlazorWebView CreateBlazorWebView(nint parentWindow) => new(parentWindow);

		/// <inheritdoc />
		protected override void OnCreated(object? sender, EventArgs e)
		{
			base.OnCreated(sender, e);

			if (AutoStart && _blazorWebView is { IsDisposed: false } view)
			{
				_ = StartAsync(view);
			}
		}

		private async Task StartAsync(BlazorWebView view)
		{
			try
			{
				await view.StartAsync();
			}
			catch (Exception ex)
			{
				OnStartFailed(ex);
			}
		}

		/// <summary>
		/// Called when the automatic start fails, typically because the WebView2 Runtime is not installed.
		/// The default implementation logs the error, shows DirectN's fatal error dialog and exits the application.
		/// </summary>
		/// <param name="exception">The failure.</param>
		protected virtual void OnStartFailed(Exception exception)
		{
			var logger = _blazorWebView?.Services?.GetService<ILogger<BlazorWebViewWindow>>() ?? NullLogger<BlazorWebViewWindow>.Instance;
			logger.LogError(exception, "Failed to start the BlazorWebView. Make sure the WebView2 Runtime is installed.");
			HandleError(exception);
		}

		/// <inheritdoc />
		protected override bool OnResized(WindowResizedType type, SIZE size)
		{
			if (_blazorWebView is { IsDisposed: false } view)
			{
				if (type == WindowResizedType.Minimized)
				{
					// Lets WebView2 throttle rendering and reduce resource usage while minimized.
					view.IsVisible = false;
				}
				else
				{
					view.IsVisible = true;
					view.Bounds = ClientRect;
				}
			}

			return base.OnResized(type, size);
		}

		/// <inheritdoc />
		protected override bool OnMoved()
		{
			if (_blazorWebView is { IsDisposed: false } view)
			{
				view.NotifyParentWindowPositionChanged();
			}

			return base.OnMoved();
		}

		/// <inheritdoc />
		protected override bool OnFocusChanged(bool setOrKill)
		{
			if (setOrKill && _blazorWebView is { IsDisposed: false } view && view.MoveFocus())
			{
				return true;
			}

			return base.OnFocusChanged(setOrKill);
		}

		/// <inheritdoc />
		protected override bool OnClosing()
		{
			if (base.OnClosing())
			{
				// Close cancelled (modal window open or a Closing handler cancelled it).
				return true;
			}

			if (_closeAfterDispose || _blazorWebView is null || _blazorWebView.IsDisposed && !_isClosing)
			{
				return false;
			}

			if (!_isClosing)
			{
				// Razor component disposal needs the WebView2 alive, so dispose the view first and close for real after.
				_isClosing = true;
				_ = DisposeViewThenCloseAsync(_blazorWebView);
			}

			return true;
		}

		private async Task DisposeViewThenCloseAsync(BlazorWebView view)
		{
			try
			{
				await view.DisposeAsync();
			}
			catch (Exception ex)
			{
				var logger = view.Services?.GetService<ILogger<BlazorWebViewWindow>>() ?? NullLogger<BlazorWebViewWindow>.Instance;
				logger.LogError(ex, "Failed to dispose the BlazorWebView while closing the window.");
			}
			finally
			{
				_closeAfterDispose = true;
				if (!IsDisposingOrDisposed)
				{
					Close();
				}
			}
		}

		/// <inheritdoc />
		protected override void OnDisposing(object? sender, CancelEventArgs e)
		{
			base.OnDisposing(sender, e);

			// The window is going away without a WM_CLOSE (e.g. DestroyWindow or Dispose). Tear the view down on a
			// best-effort basis; WebView2 copes with its parent being destroyed.
			if (!e.Cancel && _blazorWebView is { IsDisposed: false } view && IsRunningAsUIThread)
			{
				_ = view.DisposeAsync().AsTask();
			}
		}
	}
}
