using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using WindowsForms.BlazorApp.Components;
using WindowsForms.BlazorApp.Services;

namespace WindowsForms.BlazorApp;

internal sealed class MainForm : Form
{
	public MainForm(IServiceProvider services)
	{
		Text = $"Blazor in Windows Forms ({RuntimeInformation.ProcessArchitecture})";
		ClientSize = new Size(1100, 800);
		StartPosition = FormStartPosition.CenterScreen;

		var blazorWebView = new BlazorWebView
		{
			Dock = DockStyle.Fill,
			HostPage = @"wwwroot\index.html",
			Services = services,
		};
		blazorWebView.RootComponents.Add<Routes>("#app");

		blazorWebView.UrlLoading += (_, e) =>
		{
			// Example policy: never leave the app for file:// links.
			if (e.Url.IsFile)
			{
				e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
			}
		};

		// Host -> Blazor: run code inside the Blazor scope from native window events.
		Activated += (_, _) => _ = blazorWebView.TryDispatchAsync(sp => sp.GetRequiredService<HostEvents>().NotifyActivated());

		// Closing the form disposes it, and with it the BlazorWebView and its Razor components.
		Controls.Add(blazorWebView);
	}
}
