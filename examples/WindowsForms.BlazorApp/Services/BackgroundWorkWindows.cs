using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using WindowsForms.BlazorApp.Components.Background;

namespace WindowsForms.BlazorApp.Services;

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
			Dock = DockStyle.Fill,
			HostPage = @"wwwroot\index.html",
			Services = services,
		};
		view.RootComponents.Add<BackgroundWorkWindow>("#app", new Dictionary<string, object?>
		{
			[nameof(BackgroundWorkWindow.WindowId)] = id,
			[nameof(BackgroundWorkWindow.ThrowException)] = throwException,
		});

		var form = new Form
		{
			Text = $"Background work #{id} - close me while loading",
			ClientSize = new Size(520, 320),
		};
		form.Controls.Add(view);

		// A form shown with Show() is disposed when it closes, which disposes the BlazorWebView too.
		// Exceptions that escape after that reach Application.ThreadException (see Program.cs).
		form.FormClosed += (_, _) => Add($"#{id}: window closed");

		form.Show();
		Add($"#{id}: opened ({(throwException ? "throws, then DispatchExceptionAsync" : "no exception")})");
	}

	public void Add(string entry)
	{
		_log.Add($"{DateTime.Now:HH:mm:ss}  {entry}");
		Changed?.Invoke();
	}
}
