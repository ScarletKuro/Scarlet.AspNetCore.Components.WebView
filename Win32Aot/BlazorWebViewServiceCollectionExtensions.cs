using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DirectN.Extensions.Com;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebView2;

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
		/// <returns>The <see cref="IWin32AotBlazorWebViewBuilder"/>.</returns>
		public static IWin32AotBlazorWebViewBuilder AddWin32AotBlazorWebView(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.AddBlazorWebView();
			services.TryAddSingleton(new BlazorWebViewDeveloperTools { Enabled = false });
			services.TryAddSingleton(_ => new Win32AotBlazorMarkerService());
			return new Win32AotBlazorWebViewBuilder(services);
		}

		/// <summary>
		/// Enables Developer tools on the underlying WebView controls.
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/>.</param>
		/// <returns>The <see cref="IServiceCollection"/>.</returns>
		public static IServiceCollection AddBlazorWebViewDeveloperTools(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			return services.AddSingleton<BlazorWebViewDeveloperTools>(new BlazorWebViewDeveloperTools { Enabled = true });
		}

		/// <summary>
		/// Registers a pre-created <see cref="ICoreWebView2Environment"/> to be shared by every
		/// <see cref="BlazorWebView"/> resolved from the same <see cref="IServiceProvider"/>. The library
		/// will not dispose the environment; the caller owns its lifetime.
		/// </summary>
		/// <remarks>
		/// An environment supplied via <see cref="BlazorWebViewInitializingEventArgs.CoreWebView2Environment"/>
		/// on a specific view takes precedence over this shared registration.
		/// </remarks>
		public static IWin32AotBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWin32AotBlazorWebViewBuilder builder,
			IComObject<ICoreWebView2Environment> environment)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(environment);

			builder.Services.TryAddSingleton<ICoreWebView2EnvironmentProvider>(
				_ => new DefaultCoreWebView2EnvironmentProvider(environment));
			return builder;
		}

		/// <summary>
		/// Registers an asynchronous factory producing a shared <see cref="ICoreWebView2Environment"/>.
		/// The factory is invoked at most once across all <see cref="BlazorWebView"/> instances, on the UI thread of the
		/// first view that initializes; failures are cached (every caller observes the same exception). The library will
		/// not dispose the environment; the caller owns its lifetime.
		/// </summary>
		/// <remarks>
		/// The <see cref="CancellationToken"/> passed to the factory is always
		/// <see cref="CancellationToken.None"/> so a single caller's cancellation cannot poison
		/// the shared task for every other BlazorWebView.
		/// </remarks>
		public static IWin32AotBlazorWebViewBuilder UseSharedCoreWebView2Environment(
			this IWin32AotBlazorWebViewBuilder builder,
			Func<IServiceProvider, CancellationToken, ValueTask<IComObject<ICoreWebView2Environment>>> factory)
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
		public static IWin32AotBlazorWebViewBuilder UseSharedCoreWebView2Environment<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
			this IWin32AotBlazorWebViewBuilder builder)
			where TProvider : class, ICoreWebView2EnvironmentProvider
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.TryAddSingleton<ICoreWebView2EnvironmentProvider, TProvider>();
			return builder;
		}
	}
}
