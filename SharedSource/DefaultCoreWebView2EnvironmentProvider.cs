#if WINDOWS

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Microsoft.AspNetCore.Components.WebView
{
	/// <summary>
	/// Default <see cref="ICoreWebView2EnvironmentProvider"/> implementation used by the
	/// <c>UseSharedCoreWebView2Environment</c> builder extensions. Memoizes either a
	/// pre-created <see cref="CoreWebView2Environment"/> or the result of an async factory
	/// so the factory runs at most once, even under concurrent initialization.
	/// </summary>
	internal sealed class DefaultCoreWebView2EnvironmentProvider : ICoreWebView2EnvironmentProvider
	{
		private readonly Lazy<Task<CoreWebView2Environment>> _lazy;

		/// <summary>
		/// Wraps a pre-created <see cref="CoreWebView2Environment"/>. The provider never
		/// invokes a factory; every call to <see cref="GetEnvironmentAsync"/> returns the
		/// same instance.
		/// </summary>
		public DefaultCoreWebView2EnvironmentProvider(CoreWebView2Environment environment)
		{
			ArgumentNullException.ThrowIfNull(environment);

			_lazy = new Lazy<Task<CoreWebView2Environment>>(
				() => Task.FromResult(environment),
				LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <summary>
		/// Wraps an async factory. The factory is invoked at most once across all callers;
		/// subsequent calls — including those racing concurrently — observe the same result
		/// or the same exception.
		/// </summary>
		/// <remarks>
		/// Cancellation tokens passed to <see cref="GetEnvironmentAsync"/> are intentionally
		/// NOT forwarded to the factory: a single caller's cancellation would otherwise
		/// poison the cached task for every other BlazorWebView that asks for the shared
		/// environment.
		/// </remarks>
		public DefaultCoreWebView2EnvironmentProvider(
			IServiceProvider services,
			Func<IServiceProvider, CancellationToken, ValueTask<CoreWebView2Environment>> factory)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(factory);

			_lazy = new Lazy<Task<CoreWebView2Environment>>(
				() => factory(services, CancellationToken.None).AsTask(),
				LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public ValueTask<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken = default)
			=> new(_lazy.Value);
	}
}

#endif
