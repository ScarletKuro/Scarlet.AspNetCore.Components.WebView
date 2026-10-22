using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Wpf.BlazorApp.Components.Background;

namespace Wpf.BlazorApp.Services;

/// <summary>
/// Opens extra windows whose component keeps working in the background after the window is closed
/// (https://github.com/dotnet/maui/issues/34855), and keeps a log of what happened to them.
/// </summary>
public sealed class BackgroundWorkWindows(IServiceProvider services)
{
	private readonly List<string> _log = [];
	private int _nextId;

	public event Action? Changed;

	public IReadOnlyList<string> Log => _log;

	public void Open(bool throwException)
	{
		var id = ++_nextId;
		var view = new BlazorWebView
		{
			HostPage = @"wwwroot\index.html",
			Services = services,
		};
		view.RootComponents.Add(new RootComponent
		{
			Selector = "#app",
			ComponentType = typeof(BackgroundWorkWindow),
			Parameters = new Dictionary<string, object?>
			{
				[nameof(BackgroundWorkWindow.WindowId)] = id,
				[nameof(BackgroundWorkWindow.ThrowException)] = throwException,
			},
		});

		var window = new Window
		{
			Title = $"Background work #{id} - close me while loading",
			Width = 540,
			Height = 360,
			Content = view,
		};

		// Exceptions that escape after the window is gone reach Application.DispatcherUnhandledException (see App.xaml.cs).
		window.DisposeBlazorWebViewOnClose(view);
		window.Closed += (_, _) => Add($"#{id}: window closed");

		window.Show();
		Add($"#{id}: opened ({(throwException ? "throws, then DispatchExceptionAsync" : "no exception")})");
	}

	public void Add(string entry)
	{
		_log.Add($"{DateTime.Now:HH:mm:ss}  {entry}");
		Changed?.Invoke();
	}
}
