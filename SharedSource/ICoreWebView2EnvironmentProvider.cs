#if WINDOWS

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Microsoft.AspNetCore.Components.WebView
{
	/// <summary>
	/// Provides a <see cref="CoreWebView2Environment"/> that is shared across multiple
	/// <c>BlazorWebView</c> instances so they can reuse a single WebView2 environment instead
	/// of each creating its own via <see cref="CoreWebView2Environment.CreateAsync(string, string, CoreWebView2EnvironmentOptions)"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Register an implementation via <c>UseSharedCoreWebView2Environment</c> on
	/// <c>IWpfBlazorWebViewBuilder</c> or <c>IWindowsFormsBlazorWebViewBuilder</c>. When a
	/// provider is registered, every <c>BlazorWebView</c> resolved from the same
	/// <see cref="System.IServiceProvider"/> will use the environment returned by
	/// <see cref="GetEnvironmentAsync(CancellationToken)"/>.
	/// </para>
	/// <para>
	/// The caller owns the environment's lifetime. The BlazorWebView will not dispose a
	/// shared environment when the view is torn down.
	/// </para>
	/// <para>
	/// The default implementation memoizes a single environment (or the result of an async
	/// factory) and ignores the <see cref="CancellationToken"/> to avoid letting a single
	/// caller's cancellation poison the shared task for every other caller.
	/// </para>
	/// </remarks>
	public interface ICoreWebView2EnvironmentProvider
	{
		/// <summary>
		/// Returns the shared <see cref="CoreWebView2Environment"/>.
		/// </summary>
		/// <param name="cancellationToken">
		/// Optional cancellation token. Implementations MAY ignore this token when the
		/// underlying environment initialization is shared across multiple callers.
		/// </param>
		ValueTask<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken = default);
	}
}

#endif
