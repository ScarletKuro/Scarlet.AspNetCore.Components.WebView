using System;
using System.Threading;
using Scarlet.DirectN.Threading;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Resumes asynchronous work on the UI thread of a <see cref="DirectNDispatcher"/>, for a thread that has no
	/// synchronization context of its own.
	/// </summary>
	internal sealed class DispatcherSynchronizationContext(DirectNDispatcher dispatcher) : SynchronizationContext
	{
		// Like the DirectN context, posting never throws: once the dispatcher stops accepting work, it is dropped.
		public override void Post(SendOrPostCallback d, object? state) => dispatcher.TryPost(() => d(state));

		public override void Send(SendOrPostCallback d, object? state)
		{
			if (dispatcher.CheckAccess())
			{
				d(state);
			}
			else
			{
				dispatcher.InvokeAsync(() => d(state)).GetAwaiter().GetResult();
			}
		}

		public override SynchronizationContext CreateCopy() => this;
	}
}
