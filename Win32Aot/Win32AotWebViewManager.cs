// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HRESULT = DirectN.HRESULT;
using HWND = DirectN.HWND;
using DirectN.Extensions.Com;
using DirectN.Extensions.Utilities;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using WebView2;
using WebView2.Utilities;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// An implementation of <see cref="WebViewManager"/> that uses the Edge WebView2 browser, through the
	/// NativeAOT-compatible WebView2Aot bindings, to render web content.
	/// </summary>
	internal sealed class Win32AotWebViewManager : WebViewManager
	{
		// Using an IP address means that WebView2 doesn't wait for any DNS resolution,
		// making it substantially faster. Note that this isn't real HTTP traffic, since
		// we intercept all the requests within this origin.
		internal static readonly string AppHostAddress = HostAddressHelper.GetAppHostAddress();

		/// <summary>
		/// Gets the application's base URI. Defaults to <c>https://0.0.0.1/</c>.
		/// </summary>
		internal static readonly string AppOrigin = $"https://{AppHostAddress}/";

		internal static readonly Uri AppOriginUri = new(AppOrigin);

		// The code inside blazor.webview.js is meant to be agnostic to specific webview technologies,
		// so the following is an adaptor from blazor.webview.js conventions to WebView2 APIs
		private const string BlazorWebViewAdapterScript = """
			window.external = {
				sendMessage: message => {
					window.chrome.webview.postMessage(message);
				},
				receiveMessage: callback => {
					window.chrome.webview.addEventListener('message', e => callback(e.data));
				}
			};
			""";

		private static readonly Lock LoaderLock = new();

		// HRESULT_FROM_WIN32(ERROR_INVALID_STATE): returned by WebView2 calls once the WebView has been closed,
		// e.g. because its parent window was destroyed without the BlazorWebView being disposed.
		private const int WebViewClosedHResult = unchecked((int)0x8007139F);

		private readonly ILogger _logger;
		private readonly nint _parentWindow;
		private readonly Task<bool> _webviewReadyTask;
		private readonly string _contentRootRelativeToAppRoot;
		private readonly Action<UrlLoadingEventArgs> _urlLoading;
		private readonly Action<BlazorWebViewInitializingEventArgs> _blazorWebViewInitializing;
		private readonly Action<BlazorWebViewInitializedEventArgs> _blazorWebViewInitialized;
		private readonly BlazorWebViewDeveloperTools _developerTools;
		private readonly IServiceProvider _services;
		private volatile bool _isDisposed;
		private volatile bool _isWebViewClosed;

		private IComObject<ICoreWebView2Environment>? _environment;
		private bool _ownsEnvironment;
		private IComObject<ICoreWebView2Controller>? _controller;
		private IComObject<ICoreWebView2>? _webView;
		private CoreWebView2Events? _webViewEvents;

		/// <summary>
		/// Constructs an instance of <see cref="Win32AotWebViewManager"/>.
		/// </summary>
		/// <param name="parentWindow">The window handle that hosts the WebView2 controller.</param>
		/// <param name="services">A service provider containing services to be used by this class and also by application code.</param>
		/// <param name="dispatcher">A <see cref="Dispatcher"/> instance that can marshal calls to the required thread or sync context.</param>
		/// <param name="fileProvider">Provides static content to the webview.</param>
		/// <param name="jsComponents">Describes configuration for adding, removing, and updating root components from JavaScript code.</param>
		/// <param name="contentRootRelativeToAppRoot">Path to the app's content root relative to the application root directory.</param>
		/// <param name="hostPagePathWithinFileProvider">Path to the host page within the <paramref name="fileProvider"/>.</param>
		/// <param name="urlLoading">Callback invoked when a url is about to load.</param>
		/// <param name="blazorWebViewInitializing">Callback invoked before the webview is initialized.</param>
		/// <param name="blazorWebViewInitialized">Callback invoked after the webview is initialized.</param>
		/// <param name="logger">Logger to send log messages to.</param>
		internal Win32AotWebViewManager(
			nint parentWindow,
			IServiceProvider services,
			Dispatcher dispatcher,
			IFileProvider fileProvider,
			JSComponentConfigurationStore jsComponents,
			string contentRootRelativeToAppRoot,
			string hostPagePathWithinFileProvider,
			Action<UrlLoadingEventArgs> urlLoading,
			Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing,
			Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized,
			ILogger logger)
			: base(services, dispatcher, AppOriginUri, fileProvider, jsComponents, hostPagePathWithinFileProvider)
		{
			if (parentWindow == 0)
			{
				throw new ArgumentException("A valid parent window handle is required.", nameof(parentWindow));
			}

			if (services.GetService<Win32AotBlazorMarkerService>() is null)
			{
				throw new InvalidOperationException(
					"Unable to find the required services. " +
					$"Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(BlazorWebViewServiceCollectionExtensions.AddWin32AotBlazorWebView)}' in the application startup code.");
			}

			_logger = logger;
			_parentWindow = parentWindow;
			_urlLoading = urlLoading;
			_blazorWebViewInitializing = blazorWebViewInitializing;
			_blazorWebViewInitialized = blazorWebViewInitialized;
			_developerTools = services.GetRequiredService<BlazorWebViewDeveloperTools>();
			_services = services;
			_contentRootRelativeToAppRoot = contentRootRelativeToAppRoot;

			// Unfortunately the CoreWebView2 can only be instantiated asynchronously.
			// We want the external API to behave as if initalization is synchronous,
			// so keep track of a task we can await during LoadUri.
			_webviewReadyTask = TryInitializeWebView2();
		}

		/// <summary>
		/// Gets a task that completes once WebView2 is initialized. Faults if WebView2 could not be started.
		/// Completes with <see langword="false"/> if the manager was disposed during initialization.
		/// </summary>
		internal Task<bool> WebViewReadyTask => _webviewReadyTask;

		internal IComObject<ICoreWebView2Controller>? Controller => _controller;

		internal IComObject<ICoreWebView2>? WebView => _webView;

		/// <inheritdoc />
		protected override void NavigateCore(Uri absoluteUri)
		{
			if (_isDisposed)
			{
				return;
			}

			_ = Dispatcher.InvokeAsync(async () =>
			{
				if (_isDisposed)
				{
					return;
				}

				bool isWebviewInitialized;
				try
				{
					isWebviewInitialized = await _webviewReadyTask;
				}
				catch
				{
					// Initialization failures are reported through BlazorWebView.StartAsync.
					return;
				}

				if (isWebviewInitialized && !_isDisposed && !_isWebViewClosed && _webView is { } webView)
				{
					_logger.NavigatingToUri(absoluteUri);
					ThrowUnlessWebViewClosed(webView.Navigate(absoluteUri.AbsoluteUri, throwOnError: false));
				}
			});
		}

		/// <inheritdoc />
		protected override void SendMessage(string message)
		{
			if (_isDisposed || _isWebViewClosed)
			{
				return;
			}

			// The webview is null until WebView2 initialization completes
			var webView = _webView;
			if (webView is null)
			{
				return;
			}

			try
			{
				ThrowUnlessWebViewClosed(webView.PostWebMessageAsString(message, throwOnError: false));
			}
			catch (ObjectDisposedException)
			{
				// The WebView2 has been torn down (e.g. the host window was closed)
				// while an in-flight message was still pending. This is expected and safe to ignore.
			}
		}

		// Once the WebView is closed (its parent window is gone) every further call fails; there is nobody left to
		// receive messages, so they are dropped instead of surfacing as unhandled exceptions (see dotnet/maui#34855).
		private void ThrowUnlessWebViewClosed(HRESULT hr)
		{
			if (hr.Value == WebViewClosedHResult)
			{
				_isWebViewClosed = true;
				return;
			}

			hr.ThrowOnError();
		}

		private async Task<bool> TryInitializeWebView2()
		{
			var args = new BlazorWebViewInitializingEventArgs();
			_blazorWebViewInitializing?.Invoke(args);

			EnsureWebView2LoaderInitialized();

			// Precedence:
			//   1. Explicit environment on the initializing event args (per-view override).
			//   2. Shared environment registered via ICoreWebView2EnvironmentProvider in DI.
			//   3. Create a new environment using the args' folder/options.
			try
			{
				if (args.CoreWebView2Environment is not null)
				{
					_environment = args.CoreWebView2Environment;
				}
				else if (_services.GetService<ICoreWebView2EnvironmentProvider>() is { } sharedProvider)
				{
					_environment = await sharedProvider
						.GetEnvironmentAsync()
						.ConfigureAwait(true);
				}
				else
				{
					var userDataFolder = args.UserDataFolder ?? GetWebView2UserDataFolder();
					_environment = await global::WebView2.Functions.CreateCoreWebView2EnvironmentWithOptionsAsync(
						browserExecutableFolder: args.BrowserExecutableFolder,
						userDataFolder: userDataFolder,
						environmentOptions: args.EnvironmentOptions)
						.ConfigureAwait(true)
						?? throw new InvalidOperationException("WebView2 did not return an environment.");
					_ownsEnvironment = true;
				}
			}
			catch
			{
				_logger.FailedToCreateWebView2Environment();
				throw;
			}

			if (_isDisposed)
			{
				ReleaseNativeResources();
				return false;
			}

			_logger.StartingWebView2();
			var controller = await _environment
				.CreateCoreWebView2ControllerAsync(new HWND { Value = _parentWindow })
				.ConfigureAwait(true)
				?? throw new InvalidOperationException("WebView2 did not return a controller.");
			_controller = controller;

			if (_isDisposed)
			{
				ReleaseNativeResources();
				return false;
			}

			// Every read of CoreWebView2 creates a new wrapper, so read it once and keep it.
			_webView = controller.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not return a CoreWebView2.");
			_logger.StartedWebView2();

			ApplyDefaultWebViewSettings(_webView, _developerTools);

			_blazorWebViewInitialized?.Invoke(new BlazorWebViewInitializedEventArgs(_environment, controller, _webView));

			_webView.AddWebResourceRequestedFilter("*", COREWEBVIEW2_WEB_RESOURCE_CONTEXT.COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);

			// Keep the events object alive for the lifetime of the view; disposing it unsubscribes every handler.
			_webViewEvents = new CoreWebView2Events(_webView);
			_webViewEvents.WebResourceRequested += CoreWebView2_WebResourceRequested;
			_webViewEvents.NavigationStarting += CoreWebView2_NavigationStarting;
			_webViewEvents.NewWindowRequested += CoreWebView2_NewWindowRequested;

			await _webView.AddScriptToExecuteOnDocumentCreatedAsync(BlazorWebViewAdapterScript)
				.ConfigureAwait(true);

			if (_isDisposed)
			{
				return false;
			}

			_webViewEvents.WebMessageReceived += CoreWebView2_WebMessageReceived;

			return true;
		}

		private void CoreWebView2_WebResourceRequested(object? sender, ICoreWebView2WebResourceRequestedEventArgs eventArgs)
		{
			if (_isDisposed)
			{
				return;
			}

			try
			{
				HandleWebResourceRequest(eventArgs);
			}
			catch (Exception ex)
			{
				// An exception here would only be converted to an HRESULT by the COM callable wrapper. Log it instead
				// so the request fails visibly (the browser falls back to its own handling).
				_logger.LogError(ex, "Failed to handle a WebView2 web resource request.");
			}
		}

		/// <summary>
		/// Handles outbound URL requests.
		/// </summary>
		/// <param name="eventArgs">The <see cref="ICoreWebView2WebResourceRequestedEventArgs"/>.</param>
		private void HandleWebResourceRequest(ICoreWebView2WebResourceRequestedEventArgs eventArgs)
		{
			// Unlike server-side code, we get told exactly why the browser is making the request,
			// so we can be smarter about fallback. We can ensure that 'fetch' requests never result
			// in fallback, for example.
			var resourceContext = eventArgs.ResourceContext;
			var allowFallbackOnHostPage =
				resourceContext == COREWEBVIEW2_WEB_RESOURCE_CONTEXT.COREWEBVIEW2_WEB_RESOURCE_CONTEXT_DOCUMENT ||
				resourceContext == COREWEBVIEW2_WEB_RESOURCE_CONTEXT.COREWEBVIEW2_WEB_RESOURCE_CONTEXT_OTHER; // e.g., dev tools requesting page source

			string? rawUri;
			using (var request = eventArgs.Request)
			{
				rawUri = request?.Uri;
			}

			var requestUri = QueryStringHelper.RemovePossibleQueryString(rawUri);

			_logger.HandlingWebRequest(requestUri);

			if (TryGetResponseContent(requestUri, allowFallbackOnHostPage, out var statusCode, out var statusMessage, out var content, out var headers))
			{
				StaticContentHotReloadManager.TryReplaceResponseContent(_contentRootRelativeToAppRoot, requestUri, ref statusCode, ref content, headers);

				var headerString = GetHeaderString(headers);

				// The IStream owns the content stream so it's released when WebView2 releases the IStream, and
				// AutoCloseOnReadCompleteStream closes it as soon as it has been fully read.
				var contentStream = new ManagedIStream(new AutoCloseOnReadCompleteStream(content), owned: true);

				_logger.ResponseContentBeingSent(requestUri, statusCode);

				using var reasonPhrase = new Pwstr(statusMessage);
				using var headersString = new Pwstr(headerString);
				_environment!.Object.CreateWebResourceResponse(contentStream, statusCode, reasonPhrase, headersString, out var responseNative).ThrowOnError();

				using var response = new ComObject<ICoreWebView2WebResourceResponse>(responseNative);
				eventArgs.put_Response(response.Object).ThrowOnError();
			}
			else
			{
				_logger.ResponseContentNotFound(requestUri);
			}
		}

		private void CoreWebView2_WebMessageReceived(object? sender, ICoreWebView2WebMessageReceivedEventArgs args)
		{
			if (_isDisposed)
			{
				return;
			}

			string? source;
			string? message;
			try
			{
				source = args.Source;
				message = args.TryGetWebMessageAsString();
			}
			catch (Exception ex)
			{
				// Messages that are not strings (e.g. postMessage of an object from page script) are not Blazor messages.
				_logger.LogDebug(ex, "Ignoring a WebView2 web message that is not a string.");
				return;
			}

			if (source is null || message is null || !Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
			{
				return;
			}

			MessageReceived(sourceUri, message);
		}

		private void CoreWebView2_NavigationStarting(object? sender, ICoreWebView2NavigationStartingEventArgs args)
		{
			if (Uri.TryCreate(args.Uri, UriKind.RelativeOrAbsolute, out var uri))
			{
				var callbackArgs = UrlLoadingEventArgs.CreateWithDefaultLoadingStrategy(uri, AppOriginUri);

				_urlLoading?.Invoke(callbackArgs);
				_logger.NavigationEvent(uri, callbackArgs.UrlLoadingStrategy);

				if (callbackArgs.UrlLoadingStrategy == UrlLoadingStrategy.OpenExternally)
				{
					LaunchUriInExternalBrowser(uri);
				}

				args.Cancel = callbackArgs.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView;
			}
		}

		private void CoreWebView2_NewWindowRequested(object? sender, ICoreWebView2NewWindowRequestedEventArgs args)
		{
			// Intercept _blank target <a> tags to always open in device browser.
			// The ExternalLinkCallback is not invoked.
			if (Uri.TryCreate(args.Uri, UriKind.RelativeOrAbsolute, out var uri))
			{
				LaunchUriInExternalBrowser(uri);
				args.Handled = true;
			}
		}

		private void LaunchUriInExternalBrowser(Uri uri)
		{
			_logger.LaunchExternalBrowser(uri);

			try
			{
				using var launchBrowser = new Process();
				launchBrowser.StartInfo.UseShellExecute = true;
				launchBrowser.StartInfo.FileName = uri.ToString();
				launchBrowser.Start();
			}
			catch (Exception ex)
			{
				// E.g. no application is associated with the URI scheme.
				_logger.LogWarning(ex, "Failed to launch '{uri}' externally.", uri);
			}
		}

		private static string GetHeaderString(IDictionary<string, string> headers) =>
			string.Join(Environment.NewLine, headers.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

		private static void ApplyDefaultWebViewSettings(IComObject<ICoreWebView2> webView, BlazorWebViewDeveloperTools devTools)
		{
			using var settings = webView.Settings ?? throw new InvalidOperationException("WebView2 did not return settings.");

			settings.AreDevToolsEnabled = devTools.Enabled;

			// Desktop applications typically don't want the default web browser context menu
			settings.AreDefaultContextMenusEnabled = false;

			// Desktop applications almost never want to show a URL preview when hovering over a link
			settings.IsStatusBarEnabled = false;
		}

		/// <inheritdoc />
		protected override async ValueTask DisposeAsyncCore()
		{
			_isDisposed = true;
			await base.DisposeAsyncCore();
			ReleaseNativeResources();
		}

		private void ReleaseNativeResources()
		{
			Interlocked.Exchange(ref _webViewEvents, null)?.Dispose();

			var controller = Interlocked.Exchange(ref _controller, null);
			if (controller is not null)
			{
				controller.Close(throwOnError: false);
			}

			Interlocked.Exchange(ref _webView, null)?.Dispose();
			controller?.Dispose();

			var environment = Interlocked.Exchange(ref _environment, null);
			if (_ownsEnvironment)
			{
				environment?.Dispose();
			}
		}

		private static void EnsureWebView2LoaderInitialized()
		{
			if (WebView2Utilities.IsInitialized)
			{
				return;
			}

			lock (LoaderLock)
			{
				// Probes resources embedded in the entry assembly, then <app dir> and <app dir>\runtimes\win-<arch>\native.
				WebView2Utilities.Initialize(Assembly.GetEntryAssembly(), throwOnError: true);
			}
		}

		private static string? GetWebView2UserDataFolder()
		{
			// In case the application is running from a non-writable location (e.g., program files if you're not running
			// elevated), use our own convention of %LocalAppData%\YourApplicationName.WebView2.
			// We may be able to remove this if https://github.com/MicrosoftEdge/WebView2Feedback/issues/297 is fixed.
			var applicationName = Assembly.GetEntryAssembly()?.GetName().Name
				?? (Environment.ProcessPath is { } processPath ? Path.GetFileNameWithoutExtension(processPath) : null);

			if (string.IsNullOrEmpty(applicationName))
			{
				return null;
			}

			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				$"{applicationName}.WebView2");
		}
	}
}
