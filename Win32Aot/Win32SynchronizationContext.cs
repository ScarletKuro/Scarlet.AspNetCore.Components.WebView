using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// A <see cref="SynchronizationContext"/> that marshals work to the thread owning a <see cref="Win32MessageQueue"/>.
	/// WebView2 objects may only be used from the thread that created them, so <c>await</c> continuations
	/// inside Blazor and WebView2 code must come back to that thread.
	/// </summary>
	internal sealed class Win32SynchronizationContext : SynchronizationContext
	{
		private readonly Win32MessageQueue _queue;

		public Win32SynchronizationContext(Win32MessageQueue queue)
		{
			_queue = queue;
		}

		public override SynchronizationContext CreateCopy() => this;

		public override void Post(SendOrPostCallback d, object? state)
		{
			ArgumentNullException.ThrowIfNull(d);
			_queue.Post(d, state);
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			ArgumentNullException.ThrowIfNull(d);

			if (_queue.CheckAccess())
			{
				d(state);
				return;
			}

			using var done = new ManualResetEventSlim();
			ExceptionDispatchInfo? error = null;
			_queue.Post(_ =>
			{
				try
				{
					d(state);
				}
				catch (Exception ex)
				{
					error = ExceptionDispatchInfo.Capture(ex);
				}
				finally
				{
					done.Set();
				}
			}, null);

			done.Wait();
			error?.Throw();
		}
	}
}
