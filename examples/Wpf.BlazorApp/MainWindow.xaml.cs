using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using Wpf.BlazorApp.Services;

namespace Wpf.BlazorApp;

public partial class MainWindow : Window
{
	public MainWindow(IServiceProvider services)
	{
		InitializeComponent();
		Title = $"Blazor in WPF ({RuntimeInformation.ProcessArchitecture})";

		BlazorWebView.Services = services;
		BlazorWebView.UrlLoading += (_, e) =>
		{
			// Example policy: never leave the app for file:// links.
			if (e.Url.IsFile)
			{
				e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
			}
		};

		// Host -> Blazor: run code inside the Blazor scope from native window events.
		Activated += (_, _) => _ = BlazorWebView.TryDispatchAsync(sp => sp.GetRequiredService<HostEvents>().NotifyActivated());

		this.DisposeBlazorWebViewOnClose(BlazorWebView);
	}
}
