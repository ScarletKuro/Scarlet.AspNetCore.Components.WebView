using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Win32Aot.BlazorApp.Components.Background;

namespace Win32Aot.BlazorApp.Services;

/// <summary>
/// Opens extra windows whose component keeps working in the background after the window is closed
/// (https://github.com/dotnet/maui/issues/34855), and keeps a log of what happened to them.
/// </summary>
public sealed class BackgroundWorkWindows(IServiceProvider services, NativeDialogs dialogs)
{
	private readonly List<string> _log = [];
	private int _nextId;

	public event Action? Changed;

	public IReadOnlyList<string> Log => _log;

	public void Open(bool throwException)
	{
		var id = ++_nextId;
		var window = new BlazorWebViewWindow($"Background work #{id} - close me while loading");
		var view = window.BlazorWebView;
		view.HostPage = @"wwwroot\index.html";
		view.Services = services;
		view.RootComponents.Add<BackgroundWorkWindow>("#app", new Dictionary<string, object?>
		{
			[nameof(BackgroundWorkWindow.WindowId)] = id,
			[nameof(BackgroundWorkWindow.ThrowException)] = throwException,
		});

		// Reaching this handler means an exception escaped to the host after the window was gone.
		view.UnhandledException += (_, e) =>
		{
			Add($"#{id}: UNHANDLED {e.Exception.GetType().Name}: {e.Exception.Message}");
			dialogs.ShowError(e.Exception.ToString());
			e.Handled = true;
		};

		window.Destroyed += (_, _) => Add($"#{id}: window closed");

		window.ResizeClient(520, 320);
		window.Show();
		Add($"#{id}: opened ({(throwException ? "throws, then DispatchExceptionAsync" : "no exception")})");
	}

	public void Add(string entry)
	{
		_log.Add($"{DateTime.Now:HH:mm:ss}  {entry}");
		Changed?.Invoke();
	}
}
