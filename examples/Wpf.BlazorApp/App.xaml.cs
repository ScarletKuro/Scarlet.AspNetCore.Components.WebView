using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.BlazorApp.Services;

namespace Wpf.BlazorApp;

public partial class App : Application
{
	private ServiceProvider? _serviceProvider;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var services = new ServiceCollection();
		services.AddLogging(logging =>
		{
			logging.AddDebug();
#if DEBUG
			logging.SetMinimumLevel(LogLevel.Debug);
#endif
		});
		services.AddWpfBlazorWebView();
#if DEBUG
		services.AddBlazorWebViewDeveloperTools();
#endif
		services.AddSingleton<HostEvents>();
		services.AddSingleton<NativeDialogs>();
		services.AddSingleton<BackgroundWorkWindows>();

		var serviceProvider = _serviceProvider = services.BuildServiceProvider();

		// Keep the app alive and show the error instead of terminating the process.
		DispatcherUnhandledException += (_, args) =>
		{
			serviceProvider.GetRequiredService<BackgroundWorkWindows>().Add($"UNHANDLED {args.Exception.GetType().Name}: {args.Exception.Message}");
			serviceProvider.GetRequiredService<NativeDialogs>().ShowError(args.Exception.ToString());
			args.Handled = true;
		};

		MainWindow = new MainWindow(serviceProvider);
		MainWindow.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_serviceProvider?.Dispose();
		base.OnExit(e);
	}
}
