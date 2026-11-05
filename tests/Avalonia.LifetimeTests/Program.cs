using Avalonia;
using Avalonia.BlazorApp;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using LifetimeTests.Components;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalonia.LifetimeTests;

/// <summary>
/// https://github.com/ScarletKuro/WpfIssueThreading (dotnet/maui#34855) for the Avalonia example's host
/// (<c>BlazorViewHost</c> in a <c>NativeControlHost</c>): a component keeps working in the background and calls
/// InvokeAsync/DispatchExceptionAsync after its window is gone, while the application keeps running. Every scenario must
/// finish without an exception reaching the view's or Avalonia's unhandled exception handlers. Also checks what is
/// specific to Avalonia: awaited component disposal before the window closes, layout-driven resizing, and dispatching
/// through Avalonia's dispatcher. Exit code 0 means all scenarios passed.
/// </summary>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args) => AppBuilder.Configure(() => new TestApp(new LifetimeTestRunner(args)))
		.UsePlatformDetect()
		.LogToTrace()
		.StartWithClassicDesktopLifetime(args);
}

internal sealed class TestApp(LifetimeTestRunner runner) : Application
{
	public override void Initialize()
	{
		RequestedThemeVariant = ThemeVariant.Light;
		Styles.Add(new FluentTheme());
	}

	public override void OnFrameworkInitializationCompleted()
	{
		var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
		desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

		// What a real app sees as a crash (or its global error handling).
		Dispatcher.UIThread.UnhandledException += (_, e) =>
		{
			runner.ReportFatal("Dispatcher.UnhandledException", e.Exception);
			e.Handled = true;
		};

		var services = new ServiceCollection();
		services.AddLogging(b => b.AddProvider(runner.LoggerProvider).SetMinimumLevel(LogLevel.Error));
		services.AddWin32AotBlazorWebView();
		var provider = services.BuildServiceProvider();

		Dispatcher.UIThread.Post(async () =>
		{
			var exitCode = await runner.RunAsync(new Scenarios(runner, provider).All());
			await provider.DisposeAsync();
			desktop.Shutdown(exitCode);
		});

		base.OnFrameworkInitializationCompleted();
	}
}

internal sealed class Scenarios(LifetimeTestRunner runner, IServiceProvider services)
{
	public IEnumerable<(string Name, Func<Task> Run)> All()
	{
		foreach (var throwException in new[] { true, false })
		{
			var suffix = throwException ? "exception thrown" : "no exception";
			yield return ($"Close window, {suffix}", () => CloseWindow(throwException));
			yield return ($"BlazorViewHost removed from the window, window stays open, {suffix}", () => RemoveHost(throwException));
			yield return ($"Plain Window closed without awaiting disposal, {suffix}", () => ClosePlainWindow(throwException));
		}

		yield return ("Rapid open/close x10, exception thrown", () => RapidOpenClose(count: 10));
		yield return ("Close waits for asynchronous component disposal", CloseAwaitsDisposal);
		yield return ("Resize follows Avalonia layout", () => Resize(constrained: false));
		yield return ("Resize follows Avalonia layout, window smaller than its preferred size", () => Resize(constrained: true));
		yield return ("Dispatching through Avalonia's dispatcher", DispatchThroughAvalonia);
	}

	// The supported path: BlazorWindow cancels the first close, awaits the view's disposal, then closes.
	private async Task CloseWindow(bool throwException)
	{
		var window = CreateWindow(throwException);
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		await runner.WaitForBackgroundWorkFinished(1);
	}

	// NativeControlHost destroys its child when detached; the host then starts disposal without awaiting it.
	private async Task RemoveHost(bool throwException)
	{
		var window = CreateWindow(throwException);
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Content = null;
		await runner.WaitForBackgroundWorkFinished(1);
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
	}

	// Host misuse: a window without BlazorWindow's close handling, destroyed while disposal is still running.
	private async Task ClosePlainWindow(bool throwException)
	{
		var closed = false;
		var window = new Window { Title = "Avalonia.LifetimeTests (plain window)", Width = 640, Height = 360, Content = CreateHost(AppComponent(throwException)) };
		window.Closed += (_, _) => closed = true;
		window.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		window.Close();
		await LifetimeTestRunner.WaitUntil(() => closed, TimeSpan.FromSeconds(15), "window to close");
		await runner.WaitForBackgroundWorkFinished(1);
	}

	private async Task RapidOpenClose(int count)
	{
		var windows = new List<BlazorWindow>();
		for (var i = 0; i < count; i++)
		{
			var window = CreateWindow(throwException: true);
			window.Show();
			await runner.WaitForBackgroundWorkStarted(i + 1);
			window.Close();
			windows.Add(window);
		}

		await Task.WhenAll(windows.Select(w => w.ClosedTask)).WaitAsync(TimeSpan.FromSeconds(30));
		await runner.WaitForBackgroundWorkFinished(count);
	}

	private async Task CloseAwaitsDisposal()
	{
		Probe.Reset();
		var window = CreateProbeWindow();
		var disposedWhenClosed = false;
		window.Closed += (_, _) => disposedWhenClosed = Probe.Disposed;
		window.Show();
		await LifetimeTestRunner.WaitUntil(() => Probe.Rendered, TimeSpan.FromSeconds(30), "the root component to render");
		window.Close();
		await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		Require(disposedWhenClosed, "The window closed before the root component's asynchronous disposal completed.");
	}

	private async Task Resize(bool constrained)
	{
		var window = CreateProbeWindow();
		window.Width = 1280;
		window.Height = 800;
		if (constrained)
		{
			// A desktop that can't fit the window's preferred size (as on small CI screens).
			window.MinWidth = 1000;
			window.MinHeight = 520;
			window.MaxWidth = 1000;
			window.MaxHeight = 650;
		}

		window.Show();
		try
		{
			await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
			await ResizeChecks.VerifyAsync(window);
		}
		finally
		{
			window.Close();
			await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		}
	}

	private async Task DispatchThroughAvalonia()
	{
		var window = CreateProbeWindow();
		window.Show();
		try
		{
			await window.BlazorHost.Ready.WaitAsync(TimeSpan.FromSeconds(30));
			var dispatcher = window.BlazorHost.View.Dispatcher;
			await Task.Run(async () =>
			{
				await dispatcher.InvokeAsync(AssertOnAvaloniaContext);
				Require(await dispatcher.InvokeAsync(() => { AssertOnAvaloniaContext(); return 42; }) == 42, "Synchronous result");
				await dispatcher.InvokeAsync(async () =>
				{
					await Task.Delay(20);
					AssertOnAvaloniaContext();
				});
				Require(await dispatcher.InvokeAsync(async () =>
				{
					await Task.Delay(20);
					AssertOnAvaloniaContext();

					// On the UI thread, work runs inline.
					var ranInline = false;
					var inline = dispatcher.InvokeAsync(() => ranInline = true);
					Require(ranInline && inline.IsCompleted, "Work dispatched on the UI thread runs inline.");
					await inline;
					return 43;
				}) == 43, "Asynchronous result");
			});
		}
		finally
		{
			window.Close();
			await window.ClosedTask.WaitAsync(TimeSpan.FromSeconds(15));
		}

		static void AssertOnAvaloniaContext()
		{
			Dispatcher.UIThread.VerifyAccess();
			Require(SynchronizationContext.Current is AvaloniaSynchronizationContext, "Dispatched work runs on Avalonia's synchronization context.");
		}
	}

	private BlazorWindow CreateWindow(bool throwException) => CreateWindow(AppComponent(throwException));

	private BlazorWindow CreateProbeWindow() => CreateWindow(view => view.RootComponents.Add<Probe>("#app"));

	private BlazorWindow CreateWindow(Action<BlazorWebView> addComponents)
	{
		var window = new BlazorWindow(services, view => Configure(view, addComponents)) { Title = "Avalonia.LifetimeTests", Width = 640, Height = 360 };
		Observe(window.BlazorHost);
		return window;
	}

	private BlazorViewHost CreateHost(Action<BlazorWebView> addComponents)
	{
		var host = new BlazorViewHost(services, view => Configure(view, addComponents));
		Observe(host);
		return host;
	}

	// The example reports its own failures (WebView2 startup, native window messages, disposal) in its status area.
	private void Observe(BlazorViewHost host) => host.StatusChanged += status =>
	{
		if (status.StartsWith("Error:", StringComparison.Ordinal))
		{
			runner.ReportFatal("BlazorViewHost", new InvalidOperationException(status));
		}
	};

	private Action<BlazorWebView> AppComponent(bool throwException) =>
		view => view.RootComponents.Add<App>("#app", runner.Parameters(throwException));

	private void Configure(BlazorWebView view, Action<BlazorWebView> addComponents)
	{
		addComponents(view);

		// Reaching this handler is the bug: an exception escaped to the host. The example's own handler marks it handled.
		view.UnhandledException += (_, e) => runner.ReportFatal("BlazorWebView.UnhandledException", e.Exception);
	}

	internal static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
