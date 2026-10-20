using System;
using System.Runtime.InteropServices;
using DirectN.Extensions.Utilities;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Win32Aot.BlazorApp.Components;
using Win32Aot.BlazorApp.Services;

namespace Win32Aot.BlazorApp;

internal static class Program
{
	[STAThread] // WebView2 requires an STA thread
	private static void Main()
	{
		var services = new ServiceCollection();
		services.AddLogging(logging =>
		{
			logging.AddDebug();
#if DEBUG
			logging.SetMinimumLevel(LogLevel.Debug);
#endif
		});
		services.AddWin32AotBlazorWebView();
#if DEBUG
		services.AddBlazorWebViewDeveloperTools();
#endif
		services.AddSingleton<HostEvents>();
		services.AddSingleton<NativeDialogs>();

		using var serviceProvider = services.BuildServiceProvider();

		using var app = new Application();
		using var window = new BlazorWebViewWindow($"Blazor on WebView2Aot ({RuntimeInformation.ProcessArchitecture})");

		var blazorWebView = window.BlazorWebView;
		blazorWebView.HostPage = @"wwwroot\index.html";
		blazorWebView.Services = serviceProvider;
		blazorWebView.RootComponents.Add<Routes>("#app");

		blazorWebView.UnhandledException += (_, e) =>
		{
			// Keep the app alive and show the error instead of terminating the process.
			serviceProvider.GetRequiredService<NativeDialogs>().ShowError(e.Exception.ToString());
			e.Handled = true;
		};

		blazorWebView.UrlLoading += (_, e) =>
		{
			// Example policy: never leave the app for file:// links.
			if (e.Url.IsFile)
			{
				e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
			}
		};

		// Host -> Blazor: run code inside the Blazor scope from native window events.
		window.Activated += (_, _) => _ = blazorWebView.TryDispatchAsync(sp => sp.GetRequiredService<HostEvents>().NotifyActivated());

		window.ResizeClient(1100, 800);
		window.Center();
		window.Show();
		window.SetForeground();
		app.Run();
	}
}
