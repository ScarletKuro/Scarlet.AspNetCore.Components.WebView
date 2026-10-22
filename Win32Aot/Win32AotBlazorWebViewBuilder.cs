using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	internal sealed class Win32AotBlazorWebViewBuilder : IWin32AotBlazorWebViewBuilder
	{
		public IServiceCollection Services { get; }

		public Win32AotBlazorWebViewBuilder(IServiceCollection services)
		{
			Services = services;
		}
	}
}
