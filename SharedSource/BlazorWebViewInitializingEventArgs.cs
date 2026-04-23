using System;
#if WEBVIEW2_WINFORMS
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;
#elif WEBVIEW2_WPF
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
#endif

namespace Microsoft.AspNetCore.Components.WebView
{
	/// <summary>
	/// Allows configuring the underlying web view when the application is initializing.
	/// </summary>
	public class BlazorWebViewInitializingEventArgs : EventArgs
	{
#nullable disable
#if WINDOWS
		/// <summary>
		/// Gets or sets the browser executable folder path for the <see cref="WebView2Control"/>.
		/// </summary>
		public string BrowserExecutableFolder { get; set; }

		/// <summary>
		/// Gets or sets the user data folder path for the <see cref="WebView2Control"/>.
		/// </summary>
		public string UserDataFolder { get; set; }

		/// <summary>
		/// Gets or sets the environment options for the <see cref="WebView2Control"/>.
		/// </summary>
		public CoreWebView2EnvironmentOptions EnvironmentOptions { get; set; }

		/// <summary>
		/// Gets or sets a pre-created <see cref="CoreWebView2Environment"/> for the <see cref="WebView2Control"/>.
		/// When set, the BlazorWebView reuses this environment instead of creating a new one, and
		/// <see cref="BrowserExecutableFolder"/>, <see cref="UserDataFolder"/> and <see cref="EnvironmentOptions"/>
		/// are ignored. This value also takes precedence over any shared environment registered via
		/// dependency injection (see <c>UseSharedCoreWebView2Environment</c>).
		/// The caller owns the environment's lifetime; the BlazorWebView will not dispose it.
		/// </summary>
		public CoreWebView2Environment CoreWebView2Environment { get; set; }
#endif
	}
}
