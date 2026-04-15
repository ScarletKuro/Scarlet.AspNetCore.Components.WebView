using System;
using System.Threading.Tasks;
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
	/// Allows configuring the underlying web view after it has been initialized.
	/// </summary>
	public class BlazorWebViewInitializedEventArgs : EventArgs
	{
#nullable disable
#if WINDOWS
		/// <summary>
		/// Gets the <see cref="WebView2Control"/> instance that was initialized.
		/// </summary>
		public WebView2Control WebView { get; internal set; }
#endif
	}
}
