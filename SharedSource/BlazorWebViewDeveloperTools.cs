using System;

#if WEBVIEW2_WINFORMS
namespace Microsoft.AspNetCore.Components.WebView.WindowsForms
#elif WEBVIEW2_WPF
namespace Microsoft.AspNetCore.Components.WebView.Wpf
#else
#error Must define WEBVIEW2_WINFORMS or WEBVIEW2_WPF
#endif
{
	internal class BlazorWebViewDeveloperTools
	{
		public bool Enabled { get; set; } = false;
	}
}
