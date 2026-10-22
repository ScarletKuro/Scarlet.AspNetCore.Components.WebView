using System;
using DirectN.Extensions.Com;
using WebView2;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Allows configuring the underlying web view after it has been initialized.
	/// </summary>
	public class BlazorWebViewInitializedEventArgs : EventArgs
	{
		internal BlazorWebViewInitializedEventArgs(
			IComObject<ICoreWebView2Environment> environment,
			IComObject<ICoreWebView2Controller> controller,
			IComObject<ICoreWebView2> webView)
		{
			Environment = environment;
			Controller = controller;
			WebView = webView;
		}

		/// <summary>
		/// Gets the WebView2 environment in use. Owned by the <see cref="BlazorWebView"/> (or by the caller when supplied
		/// externally); do not dispose it.
		/// </summary>
		public IComObject<ICoreWebView2Environment> Environment { get; }

		/// <summary>
		/// Gets the WebView2 controller. Owned by the <see cref="BlazorWebView"/>; do not dispose or close it.
		/// </summary>
		public IComObject<ICoreWebView2Controller> Controller { get; }

		/// <summary>
		/// Gets the <see cref="ICoreWebView2"/> instance that was initialized. Owned by the <see cref="BlazorWebView"/>;
		/// do not dispose it.
		/// </summary>
		public IComObject<ICoreWebView2> WebView { get; }
	}
}
