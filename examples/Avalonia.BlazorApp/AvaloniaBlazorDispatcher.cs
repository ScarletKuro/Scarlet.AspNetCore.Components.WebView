using Avalonia.Threading;
using BlazorDispatcher = Microsoft.AspNetCore.Components.Dispatcher;
using UiDispatcher = Avalonia.Threading.Dispatcher;

namespace Avalonia.BlazorApp;

// Keeps both initial callbacks and their await continuations on Avalonia's dispatcher.
// The host owns exception reporting because Blazor also dispatches fire-and-forget work.
internal sealed class AvaloniaBlazorDispatcher(UiDispatcher dispatcher, Action<Exception> reportException) : BlazorDispatcher
{
	public override bool CheckAccess() => dispatcher.CheckAccess();

	public override Task InvokeAsync(Action workItem)
	{
		ArgumentNullException.ThrowIfNull(workItem);
		return InvokeAsync(() =>
		{
			workItem();
			return Task.FromResult(0);
		});
	}

	public override Task InvokeAsync(Func<Task> workItem)
	{
		ArgumentNullException.ThrowIfNull(workItem);
		return InvokeAsync(async () =>
		{
			await workItem();
			return 0;
		});
	}

	public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
	{
		ArgumentNullException.ThrowIfNull(workItem);
		return InvokeAsync(() => Task.FromResult(workItem()));
	}

	public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
	{
		ArgumentNullException.ThrowIfNull(workItem);
		try
		{
			if (CheckAccess())
			{
				using (AvaloniaSynchronizationContext.Ensure(dispatcher, DispatcherPriority.Normal))
				{
					return await workItem();
				}
			}

			// Avalonia's Func<Task<T>> overload unwraps the task, including asynchronous failures.
			return await dispatcher.InvokeAsync(workItem, DispatcherPriority.Normal);
		}
		catch (Exception ex)
		{
			// Post so that reporting is on the UI thread and outside WebView2's native callback stack.
			dispatcher.Post(() => reportException(ex));
			throw;
		}
	}
}
