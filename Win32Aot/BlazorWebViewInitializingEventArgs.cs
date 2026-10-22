using System;
using DirectN.Extensions.Com;
using WebView2;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Allows configuring the underlying web view when the application is initializing.
	/// </summary>
	public class BlazorWebViewInitializingEventArgs : EventArgs
	{
		/// <summary>
		/// Gets or sets the browser executable folder path. When <see langword="null"/>, the installed Evergreen
		/// WebView2 Runtime is used.
		/// </summary>
		public string? BrowserExecutableFolder { get; set; }

		/// <summary>
		/// Gets or sets the user data folder path. When <see langword="null"/>, defaults to
		/// <c>%LocalAppData%\{ApplicationName}.WebView2</c>.
		/// </summary>
		public string? UserDataFolder { get; set; }

		/// <summary>
		/// Gets or sets the environment options used when the <see cref="BlazorWebView"/> creates its own environment.
		/// </summary>
		/// <remarks>
		/// The caller owns the options object and may dispose it once the <see cref="BlazorWebView.BlazorWebViewInitialized"/>
		/// event has been raised. Note that <see cref="CoreWebView2EnvironmentOptions"/> reports <c>default</c> for every
		/// property that was not explicitly set; set e.g. <c>EnableTrackingPrevention</c> explicitly when it matters.
		/// </remarks>
		public CoreWebView2EnvironmentOptions? EnvironmentOptions { get; set; }

		/// <summary>
		/// Gets or sets a pre-created <see cref="ICoreWebView2Environment"/>.
		/// When set, the <see cref="BlazorWebView"/> reuses this environment instead of creating a new one, and
		/// <see cref="BrowserExecutableFolder"/>, <see cref="UserDataFolder"/> and <see cref="EnvironmentOptions"/>
		/// are ignored. This value also takes precedence over any shared environment registered via
		/// dependency injection (see <c>UseSharedCoreWebView2Environment</c>).
		/// The caller owns the environment's lifetime; the <see cref="BlazorWebView"/> will not dispose it.
		/// </summary>
		public IComObject<ICoreWebView2Environment>? CoreWebView2Environment { get; set; }
	}
}
