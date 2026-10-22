using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using LifetimeTests.Components;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wpf.LifetimeTests;

/// <summary>
/// https://github.com/ScarletKuro/WpfIssueThreading (dotnet/maui#34855) for the WPF host: a component keeps working in
/// the background and calls InvokeAsync/DispatchExceptionAsync after its window is gone, while the application keeps
/// running (ShutdownMode="OnExplicitShutdown"). Every scenario must finish without an exception reaching
/// Application.DispatcherUnhandledException. Exit code 0 means all scenarios passed.
/// </summary>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		var runner = new LifetimeTestRunner(args);

		var services = new ServiceCollection();
		services.AddLogging(b => b.AddProvider(runner.LoggerProvider).SetMinimumLevel(LogLevel.Error));
		services.AddWpfBlazorWebView();
		using var serviceProvider = services.BuildServiceProvider();

		var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

		// What a real app sees as a crash (or its global error dialog).
		app.DispatcherUnhandledException += (_, e) =>
		{
			runner.ReportFatal("Application.DispatcherUnhandledException", e.Exception);
			e.Handled = true;
		};

		app.Startup += async (_, _) => app.Shutdown(await runner.RunAsync(new Scenarios(runner, serviceProvider).All()));
		return app.Run();
	}
}

internal sealed class Scenarios(LifetimeTestRunner runner, IServiceProvider services)
{
	public IEnumerable<(string Name, Func<Task> Run)> All()
	{
		foreach (var throwException in new[] { true, false })
		{
			var suffix = throwException ? "exception thrown" : "no exception";
			yield return ($"Close window, DisposeAsync before it closes, {suffix}", () => CloseWindow(throwException, dispose: true));
			yield return ($"Close window, DisposeAsync NOT called, {suffix}", () => CloseWindow(throwException, dispose: false));
			yield return ($"Modal window (ShowDialog) closed, DisposeAsync NOT called, {suffix}", () => ModalNeverDisposed(throwException));
			yield return ($"BlazorWebView removed and disposed, window stays open, {suffix}", () => DisposeViewOnly(throwException));
		}

		yield return ("Rapid open/close x10 with DisposeAsync, exception thrown", () => RapidOpenClose(count: 10, dispose: true));
		yield return ("Rapid open/close x10 without DisposeAsync, exception thrown", () => RapidOpenClose(count: 10, dispose: false));
	}

	private async Task CloseWindow(bool throwException, bool dispose)
	{
		var window = CreateWindow(throwException, dispose, out _);
		var closed = false;
		window.Closed += (_, _) => closed = true;
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Close();
		await LifetimeTestRunner.WaitUntil(() => closed, TimeSpan.FromSeconds(15), "window to close");
		await runner.WaitForBackgroundWorkFinished(1);
	}

	// The shape of the original repro: MainWindow.ShowDialog() and the DisposeAsync call commented out.
	private async Task ModalNeverDisposed(bool throwException)
	{
		var window = CreateWindow(throwException, dispose: false, out _);
		window.ContentRendered += async (_, _) =>
		{
			await runner.WaitForBackgroundWorkStarted(1);
			await Task.Delay(runner.Hold);
			window.Close();
		};

		window.ShowDialog();
		await runner.WaitForBackgroundWorkFinished(1);
	}

	private async Task DisposeViewOnly(bool throwException)
	{
		var window = CreateWindow(throwException, dispose: false, out var view);
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Content = null;
		await view.DisposeAsync();
		await runner.WaitForBackgroundWorkFinished(1);
		window.Close();
	}

	private async Task RapidOpenClose(int count, bool dispose)
	{
		var closed = 0;
		for (var i = 0; i < count; i++)
		{
			var window = CreateWindow(throwException: true, dispose, out _);
			window.Closed += (_, _) => closed++;
			window.Show();
			await runner.WaitForBackgroundWorkStarted(i + 1);
			window.Close();
		}

		await LifetimeTestRunner.WaitUntil(() => closed == count, TimeSpan.FromSeconds(30), "windows to close");
		await runner.WaitForBackgroundWorkFinished(count);
	}

	private Window CreateWindow(bool throwException, bool dispose, out BlazorWebView view)
	{
		var blazorWebView = view = new BlazorWebView
		{
			HostPage = @"wwwroot\index.html",
			Services = services,
		};
		view.RootComponents.Add(new RootComponent
		{
			Selector = "#app",
			ComponentType = typeof(App),
			Parameters = runner.Parameters(throwException),
		});

		var window = new Window { Title = "Wpf.LifetimeTests", Width = 640, Height = 360, Content = view };
		if (dispose)
		{
			// Same as examples/Wpf.BlazorApp: close the window only after DisposeAsync finished.
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
				_ = window.Dispatcher.BeginInvoke(window.Close);
			};
		}

		return window;
	}
}
