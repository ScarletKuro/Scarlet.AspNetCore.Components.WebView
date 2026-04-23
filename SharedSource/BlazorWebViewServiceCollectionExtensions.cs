using System;
#if WEBVIEW2_WINFORMS
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
#elif WEBVIEW2_WPF
using Microsoft.AspNetCore.Components.WebView.Wpf;
#else
#error Must define WEBVIEW2_WINFORMS or WEBVIEW2_WPF
#endif
#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
	/// <summary>
	/// Extension methods to <see cref="IServiceCollection"/>.
	/// </summary>
	public static class BlazorWebViewServiceCollectionExtensions
	{
		/// <summary>
		/// Configures <see cref="IServiceCollection"/> to add support for <see cref="BlazorWebView"/>.
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/>.</param>
		/// <returns>The <see cref="IServiceCollection"/>.</returns>
#if WEBVIEW2_WINFORMS
		public static IWindowsFormsBlazorWebViewBuilder AddWindowsFormsBlazorWebView(this IServiceCollection services)
#elif WEBVIEW2_WPF
		public static IWpfBlazorWebViewBuilder AddWpfBlazorWebView(this IServiceCollection services)
#endif
		{
			services.AddBlazorWebView();
			services.TryAddSingleton(new BlazorWebViewDeveloperTools { Enabled = false });
#if WEBVIEW2_WINFORMS
			services.TryAddSingleton(_ => new WindowsFormsBlazorMarkerService());
			return new WindowsFormsBlazorWebViewBuilder(services);
#elif WEBVIEW2_WPF
			services.TryAddSingleton(_ => new WpfBlazorMarkerService());
			return new WpfBlazorWebViewBuilder(services);
#endif
		}

		/// <summary>
		/// Enables Developer tools on the underlying WebView controls.
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/>.</param>
		/// <returns>The <see cref="IServiceCollection"/>.</returns>
		public static IServiceCollection AddBlazorWebViewDeveloperTools(this IServiceCollection services)
		{
			return services.AddSingleton<BlazorWebViewDeveloperTools>(new BlazorWebViewDeveloperTools { Enabled = true });
		}

#if WINDOWS
		/// <summary>
		/// Registers a pre-created <see cref="CoreWebView2Environment"/> to be shared by every
		/// <c>BlazorWebView</c> resolved from the same <see cref="IServiceProvider"/>. The library
		/// will not dispose the environment; the caller owns its lifetime.
		/// </summary>
		/// <remarks>
		/// An environment supplied via <see cref="BlazorWebViewInitializingEventArgs.CoreWebView2Environment"/>
		/// on a specific view takes precedence over this shared registration.
		/// </remarks>
#if WEBVIEW2_WINFORMS
		public static IWindowsFormsBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWindowsFormsBlazorWebViewBuilder builder,
			CoreWebView2Environment environment)
#elif WEBVIEW2_WPF
		public static IWpfBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWpfBlazorWebViewBuilder builder,
			CoreWebView2Environment environment)
#endif
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(environment);

			builder.Services.TryAddSingleton<ICoreWebView2EnvironmentProvider>(
				_ => new DefaultCoreWebView2EnvironmentProvider(environment));
			return builder;
		}

		/// <summary>
		/// Registers an asynchronous factory producing a shared <see cref="CoreWebView2Environment"/>.
		/// The factory is invoked at most once across all <c>BlazorWebView</c> instances; failures
		/// are cached (every caller observes the same exception). The library will not dispose the
		/// environment; the caller owns its lifetime.
		/// </summary>
		/// <remarks>
		/// The <see cref="CancellationToken"/> passed to the factory is always
		/// <see cref="CancellationToken.None"/> so a single caller's cancellation cannot poison
		/// the shared task for every other BlazorWebView.
		/// </remarks>
#if WEBVIEW2_WINFORMS
		public static IWindowsFormsBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWindowsFormsBlazorWebViewBuilder builder,
			Func<IServiceProvider, CancellationToken, ValueTask<CoreWebView2Environment>> factory)
#elif WEBVIEW2_WPF
		public static IWpfBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWpfBlazorWebViewBuilder builder,
			Func<IServiceProvider, CancellationToken, ValueTask<CoreWebView2Environment>> factory)
#endif
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(factory);

			builder.Services.TryAddSingleton<ICoreWebView2EnvironmentProvider>(
				sp => new DefaultCoreWebView2EnvironmentProvider(sp, factory));
			return builder;
		}

		/// <summary>
		/// Registers a custom <see cref="ICoreWebView2EnvironmentProvider"/> implementation as a
		/// singleton to supply the shared environment.
		/// </summary>
		/// <typeparam name="TProvider">The provider implementation type.</typeparam>
#if WEBVIEW2_WINFORMS
		public static IWindowsFormsBlazorWebViewBuilder UseSharedCoreWebView2Environment<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
			this IWindowsFormsBlazorWebViewBuilder builder)
			where TProvider : class, ICoreWebView2EnvironmentProvider
#elif WEBVIEW2_WPF
		public static IWpfBlazorWebViewBuilder UseSharedCoreWebView2Environment<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
			this IWpfBlazorWebViewBuilder builder)
			where TProvider : class, ICoreWebView2EnvironmentProvider
#endif
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.TryAddSingleton<ICoreWebView2EnvironmentProvider, TProvider>();
			return builder;
		}
#endif
	}
}
