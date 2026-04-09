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
#endif
	}
}
