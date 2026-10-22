using System.ComponentModel;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace Wpf.BlazorApp;

internal static class WindowExtensions
{
	/// <summary>
	/// The WPF <see cref="BlazorWebView"/> is not disposed with its window. This closes the window only after
	/// <see cref="BlazorWebView.DisposeAsync"/> has finished, so Razor components are disposed while the WebView still
	/// exists.
	/// </summary>
	public static void DisposeBlazorWebViewOnClose(this Window window, BlazorWebView blazorWebView)
	{
		var disposed = false;
		window.Closing += async (_, e) =>
		{
			if (disposed)
			{
				return;
			}

			e.Cancel = true;
			disposed = true;
			await blazorWebView.DisposeAsync();

			// DisposeAsync can complete synchronously, and Close() can't be called from inside Closing.
			_ = window.Dispatcher.BeginInvoke(window.Close);
		};
	}
}
