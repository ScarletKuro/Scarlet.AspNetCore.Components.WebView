using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Scarlet.DirectN.Threading;

namespace Avalonia.BlazorApp;

// Avalonia's dispatcher behind the DirectNDispatcher contract, so BlazorWebView schedules its work there. The view keeps
// Blazor's dispatching rules on top: inline work on the UI thread, and tasks that fault when work is dropped.
internal sealed class AvaloniaUiDispatcher(Dispatcher dispatcher) : DirectNDispatcher
{
	public override bool CheckAccess() => dispatcher.CheckAccess();

	protected override bool TryEnqueue(SendOrPostCallback callback, object? state, Action<object?> onAbandoned)
	{
		// InvokeAsync returns the operation, so dropped work can be observed. Its exceptions would only fault the
		// operation's task, so rethrow them through Post, which reaches Dispatcher.UnhandledException.
		var operation = dispatcher.InvokeAsync(() =>
		{
			try
			{
				callback(state);
			}
			catch (Exception ex)
			{
				dispatcher.Post(ExceptionDispatchInfo.Capture(ex).Throw, DispatcherPriority.Send);
			}
		}, DispatcherPriority.Normal);

		// Avalonia rejects work after its dispatcher has shut down by aborting the operation before returning it.
		if (operation.Status == DispatcherOperationStatus.Aborted)
		{
			return false;
		}

		// Shutdown aborts every operation still queued.
		operation.GetTask().ContinueWith(_ => onAbandoned(state), CancellationToken.None,
			TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		return true;
	}
}
