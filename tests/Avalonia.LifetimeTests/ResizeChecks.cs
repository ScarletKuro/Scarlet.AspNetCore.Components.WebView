using Avalonia.BlazorApp;
using WebView2.Utilities;

namespace Avalonia.LifetimeTests;

/// <summary>
/// Avalonia lays out the native child HWND; the example forwards its size to WebView2. Waits until Avalonia's layout, the
/// child HWND, the WebView2 controller and the browser viewport agree, while the window shrinks, grows back and moves.
/// </summary>
internal static class ResizeChecks
{
	public static async Task VerifyAsync(BlazorWindow window)
	{
		// A shown window doesn't mean Avalonia has finished sizing its native child yet.
		await WaitForWebViewSizeAsync(window, "initial layout");
		var originalSize = window.ClientSize;
		var originalRect = window.BlazorHost.NativeClientRect;
		var originalMinWidth = window.MinWidth;
		var originalMinHeight = window.MinHeight;
		var smallerSize = new Size(originalSize.Width - 120, originalSize.Height - 80);
		try
		{
			// Shrink and restore within the size the desktop already accepted: growing beyond it can be clamped on a small
			// desktop. Allow shrinking below the window's minimum when it was already constrained to it.
			window.MinWidth = Math.Min(originalMinWidth, smallerSize.Width);
			window.MinHeight = Math.Min(originalMinHeight, smallerSize.Height);
			window.Width = smallerSize.Width;
			window.Height = smallerSize.Height;
			await WaitForWebViewSizeAsync(window, "smaller WebView size", smallerSize);
			var smallerRect = window.BlazorHost.NativeClientRect;
			Scenarios.Require(smallerRect.right < originalRect.right && smallerRect.bottom < originalRect.bottom,
				"The native WebView must shrink in both dimensions.");

			window.Width = originalSize.Width;
			window.Height = originalSize.Height;
			await WaitForWebViewSizeAsync(window, "restored WebView size", originalSize);
			var restoredRect = window.BlazorHost.NativeClientRect;
			Scenarios.Require(Math.Abs(restoredRect.right - originalRect.right) <= 1 && Math.Abs(restoredRect.bottom - originalRect.bottom) <= 1,
				"The restored native WebView must match its initial size.");
		}
		finally
		{
			window.MinWidth = originalMinWidth;
			window.MinHeight = originalMinHeight;
		}

		window.Position = new PixelPoint(window.Position.X + 20, window.Position.Y + 20);
		await WaitForWebViewSizeAsync(window, "WebView layout after moving the window");
	}

	private static async Task WaitForWebViewSizeAsync(BlazorWindow window, string description, Size? expectedClientSize = null)
	{
		await WaitUntilAsync(() =>
		{
			var host = window.BlazorHost;
			var native = host.NativeClientRect;
			var controller = host.View.Bounds;
			var scale = window.RenderScaling;
			return Task.FromResult(window.IsMeasureValid && window.IsArrangeValid && host.IsMeasureValid && host.IsArrangeValid
				&& (expectedClientSize is not { } expected ||
					Math.Abs(window.ClientSize.Width - expected.Width) <= 1 && Math.Abs(window.ClientSize.Height - expected.Height) <= 1)
				&& native.right > 0 && native.bottom > 0
				&& Math.Abs(native.right - host.Bounds.Width * scale) <= 1
				&& Math.Abs(native.bottom - host.Bounds.Height * scale) <= 1
				&& native.right == controller.right && native.bottom == controller.bottom);
		}, description, () => Describe(window, expectedClientSize));

		await WaitUntilAsync(async () =>
		{
			var native = window.BlazorHost.NativeClientRect;
			return await window.BlazorHost.View.WebView!.ExecuteScriptAsync(
				$"Math.abs(Math.round(innerWidth * devicePixelRatio) - {native.right}) <= 2 && " +
				$"Math.abs(Math.round(innerHeight * devicePixelRatio) - {native.bottom}) <= 2") == "true";
		}, $"browser viewport for {description}", () => Describe(window, expectedClientSize));
	}

	private static string Describe(BlazorWindow window, Size? target)
	{
		var native = window.BlazorHost.NativeClientRect;
		var controller = window.BlazorHost.View.Bounds;
		return $"target={target}; client={window.ClientSize}; layout={window.BlazorHost.Bounds}; " +
			$"native={native.right}x{native.bottom}; controller={controller.right}x{controller.bottom}; " +
			$"scale={window.RenderScaling}; state={window.WindowState}; limits={window.MaxWidth}x{window.MaxHeight}";
	}

	private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description, Func<string> diagnostics)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		try
		{
			while (!await condition().WaitAsync(timeout.Token))
			{
				await Task.Delay(50, timeout.Token);
			}
		}
		catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
		{
			throw new TimeoutException($"Timed out waiting for {description}. {diagnostics()}", ex);
		}
	}
}
