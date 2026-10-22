using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LifetimeTests.Components;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WindowsForms.LifetimeTests;

/// <summary>
/// https://github.com/ScarletKuro/WpfIssueThreading (dotnet/maui#34855) for the Windows Forms host: a component keeps
/// working in the background and calls InvokeAsync/DispatchExceptionAsync after its form is gone, while the
/// application keeps running. Every scenario must finish without an exception reaching Application.ThreadException.
/// Exit code 0 means all scenarios passed.
/// </summary>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		var runner = new LifetimeTestRunner(args);

		// What a real app sees as a crash (or the default "unhandled exception" dialog).
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (_, e) => runner.ReportFatal("Application.ThreadException", e.Exception);

		var services = new ServiceCollection();
		services.AddLogging(b => b.AddProvider(runner.LoggerProvider).SetMinimumLevel(LogLevel.Error));
		services.AddWindowsFormsBlazorWebView();
		using var serviceProvider = services.BuildServiceProvider();

		var scenarios = new Scenarios(runner, serviceProvider);
		var exitCode = 1;
		using var context = new ApplicationContext();

		var synchronizationContext = new WindowsFormsSynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(synchronizationContext);
		synchronizationContext.Post(async _ =>
		{
			exitCode = await runner.RunAsync(scenarios.All());
			context.ExitThread();
		}, null);

		Application.Run(context);
		return exitCode;
	}
}

internal sealed class Scenarios(LifetimeTestRunner runner, IServiceProvider services)
{
	public IEnumerable<(string Name, Func<Task> Run)> All()
	{
		foreach (var throwException in new[] { true, false })
		{
			var suffix = throwException ? "exception thrown" : "no exception";
			yield return ($"Close form, {suffix}", () => CloseForm(throwException));
			yield return ($"Dispose form without closing it, {suffix}", () => DisposeForm(throwException));
			yield return ($"Modal form closed, never disposed, {suffix}", () => ModalNeverDisposed(throwException));
			yield return ($"BlazorWebView removed and disposed, form stays open, {suffix}", () => DisposeControlOnly(throwException));
		}

		yield return ("Rapid open/close x10, exception thrown", () => RapidOpenClose(count: 10));
	}

	// The supported path: a form shown with Show() is disposed on close, and disposes the BlazorWebView with it.
	private async Task CloseForm(bool throwException)
	{
		var form = CreateForm(throwException, out _);
		form.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		form.Close();
		await LifetimeTestRunner.WaitUntil(() => form.IsDisposed, TimeSpan.FromSeconds(15), "form to be disposed");
		await runner.WaitForBackgroundWorkFinished(1);
	}

	private async Task DisposeForm(bool throwException)
	{
		var form = CreateForm(throwException, out _);
		form.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		form.Dispose();
		await runner.WaitForBackgroundWorkFinished(1);
	}

	// Closing a modal form only hides it; nothing disposes the form or the BlazorWebView ("DisposeAsync NOT called").
	private async Task ModalNeverDisposed(bool throwException)
	{
		var form = CreateForm(throwException, out _);
		form.Shown += async (_, _) =>
		{
			await runner.WaitForBackgroundWorkStarted(1);
			await Task.Delay(runner.Hold);
			form.Close();
		};

		form.ShowDialog();
		await runner.WaitForBackgroundWorkFinished(1);
		GC.KeepAlive(form);
	}

	private async Task DisposeControlOnly(bool throwException)
	{
		var form = CreateForm(throwException, out var view);
		form.Show();
		await runner.WaitForBackgroundWorkStarted(1);
		await Task.Delay(runner.Hold);
		form.Controls.Remove(view);
		view.Dispose();
		await runner.WaitForBackgroundWorkFinished(1);
		form.Dispose();
	}

	private async Task RapidOpenClose(int count)
	{
		var forms = new List<Form>();
		for (var i = 0; i < count; i++)
		{
			var form = CreateForm(throwException: true, out _);
			form.Show();
			await runner.WaitForBackgroundWorkStarted(i + 1);
			form.Close();
			forms.Add(form);
		}

		await LifetimeTestRunner.WaitUntil(() => forms.TrueForAll(f => f.IsDisposed), TimeSpan.FromSeconds(30), "forms to be disposed");
		await runner.WaitForBackgroundWorkFinished(count);
	}

	private Form CreateForm(bool throwException, out BlazorWebView view)
	{
		view = new BlazorWebView
		{
			Dock = DockStyle.Fill,
			HostPage = @"wwwroot\index.html",
			Services = services,
		};
		view.RootComponents.Add<App>("#app", runner.Parameters(throwException));

		var form = new Form { Text = "WindowsForms.LifetimeTests", ClientSize = new Size(640, 360) };
		form.Controls.Add(view);
		return form;
	}
}
