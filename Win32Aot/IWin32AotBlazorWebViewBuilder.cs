using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// A builder for Win32 (WebView2Aot) Blazor WebViews.
	/// </summary>
	public interface IWin32AotBlazorWebViewBuilder
	{
		/// <summary>
		/// Gets the builder service collection.
		/// </summary>
		IServiceCollection Services { get; }
	}
}
