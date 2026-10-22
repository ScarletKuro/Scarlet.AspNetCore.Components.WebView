using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsForms.BlazorApp.Services;

namespace WindowsForms.BlazorApp;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		ApplicationConfiguration.Initialize();

		var services = new ServiceCollection();
		services.AddLogging(logging =>
		{
			logging.AddDebug();
#if DEBUG
			logging.SetMinimumLevel(LogLevel.Debug);
#endif
		});
		services.AddWindowsFormsBlazorWebView();
#if DEBUG
		services.AddBlazorWebViewDeveloperTools();
#endif
		services.AddSingleton<HostEvents>();
		services.AddSingleton<NativeDialogs>();
		services.AddSingleton<BackgroundWorkWindows>();

		using var serviceProvider = services.BuildServiceProvider();

		// Keep the app alive and show the error instead of terminating the process.
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (_, e) =>
		{
			serviceProvider.GetRequiredService<BackgroundWorkWindows>().Add($"UNHANDLED {e.Exception.GetType().Name}: {e.Exception.Message}");
			serviceProvider.GetRequiredService<NativeDialogs>().ShowError(e.Exception.ToString());
		};

		Application.Run(new MainForm(serviceProvider));
	}
}
