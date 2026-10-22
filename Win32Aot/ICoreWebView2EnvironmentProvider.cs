using System.Threading;
using System.Threading.Tasks;
using DirectN.Extensions.Com;
using WebView2;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Provides an <see cref="ICoreWebView2Environment"/> that is shared across multiple
	/// <see cref="BlazorWebView"/> instances so they can reuse a single WebView2 environment
	/// (and browser process) instead of each creating its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Register an implementation via <c>UseSharedCoreWebView2Environment</c> on
	/// <see cref="IWin32AotBlazorWebViewBuilder"/>. When a provider is registered, every
	/// <see cref="BlazorWebView"/> resolved from the same <see cref="System.IServiceProvider"/> will use the
	/// environment returned by <see cref="GetEnvironmentAsync(CancellationToken)"/>.
	/// </para>
	/// <para>
	/// The caller owns the environment's lifetime. The <see cref="BlazorWebView"/> will not dispose a
	/// shared environment when the view is torn down.
	/// </para>
	/// <para>
	/// WebView2 objects are bound to the thread that created them, so the environment must be created
	/// on the same UI thread as the views that use it.
	/// </para>
	/// </remarks>
	public interface ICoreWebView2EnvironmentProvider
	{
		/// <summary>
		/// Returns the shared <see cref="ICoreWebView2Environment"/>.
		/// </summary>
		/// <param name="cancellationToken">
		/// Optional cancellation token. Implementations MAY ignore this token when the
		/// underlying environment initialization is shared across multiple callers.
		/// </param>
		ValueTask<IComObject<ICoreWebView2Environment>> GetEnvironmentAsync(CancellationToken cancellationToken = default);
	}
}
