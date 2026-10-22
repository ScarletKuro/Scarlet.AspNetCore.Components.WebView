using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using static Microsoft.AspNetCore.Components.WebView.Win32Aot.NativeMethods;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// A work queue bound to a UI thread. Work items are executed by that thread's Win32 message loop
	/// through a hidden message-only window, so it works with any message pump (DirectN <c>Application.Run</c>,
	/// WinForms, WPF or a hand-written <c>GetMessage</c> loop).
	/// </summary>
	/// <remarks>
	/// There is one queue per thread, shared by every <see cref="BlazorWebView"/> on that thread, and it lives as long as
	/// the thread: <c>await</c> continuations scheduled while a view is being torn down must still run, so the queue
	/// cannot go away with a view (WinForms' synchronization context behaves the same way). The OS destroys the
	/// message-only window when the thread exits.
	/// </remarks>
	internal sealed unsafe class Win32MessageQueue
	{
		private const string WindowClassName = "Scarlet.AspNetCore.Components.WebView.Win32Aot.Dispatcher";

		// Upper bound of work items run per wake-up so a flood of posts can't starve input and paint messages.
		private const int MaxItemsPerWakeUp = 256;

		private static readonly uint WakeMessage = RegisterWindowMessageW(WindowClassName);
		private static readonly Lock ClassRegistrationLock = new();
		private static bool _classRegistered;

		[ThreadStatic]
		private static Win32MessageQueue? t_current;

		private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
		private readonly nint _hwnd;
		private int _wakePending;

		private Win32MessageQueue()
		{
			ThreadId = Environment.CurrentManagedThreadId;
			SynchronizationContext = new Win32SynchronizationContext(this);

			if (WakeMessage == 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}

			EnsureWindowClassRegistered();

			var hwnd = CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, GetModuleHandleW(null), 0);
			if (hwnd == 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}

			// The queue lives for the rest of the thread's lifetime, so the handle is intentionally never freed.
			SetWindowLongPtrW(hwnd, GWLP_USERDATA, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
			_hwnd = hwnd;
		}

		/// <summary>
		/// Raised on the owning thread when a posted work item throws. Handlers set
		/// <see cref="BlazorWebViewUnhandledExceptionEventArgs.Handled"/> to swallow it; otherwise the exception is
		/// rethrown as an unhandled exception.
		/// </summary>
		public event EventHandler<BlazorWebViewUnhandledExceptionEventArgs>? UnhandledException;

		/// <summary>
		/// Gets the managed thread id of the thread that owns the queue.
		/// </summary>
		public int ThreadId { get; }

		/// <summary>
		/// Gets the <see cref="System.Threading.SynchronizationContext"/> that posts to this queue.
		/// </summary>
		public Win32SynchronizationContext SynchronizationContext { get; }

		/// <summary>
		/// Gets the queue of the calling thread, creating it on first use.
		/// </summary>
		public static Win32MessageQueue GetOrCreateForCurrentThread() => t_current ??= new Win32MessageQueue();

		public bool CheckAccess() => Environment.CurrentManagedThreadId == ThreadId;

		/// <summary>
		/// Queues <paramref name="callback"/> for execution on the owning thread.
		/// </summary>
		public void Post(SendOrPostCallback callback, object? state)
		{
			ArgumentNullException.ThrowIfNull(callback);

			_queue.Enqueue((callback, state));
			Wake();
		}

		private void Wake()
		{
			if (Interlocked.Exchange(ref _wakePending, 1) == 1)
			{
				// A wake-up message is already in flight; it will pick up this item too.
				return;
			}

			if (!PostMessageW(_hwnd, WakeMessage, 0, 0))
			{
				// Typically the thread has exited (window destroyed) or its message queue is full.
				Interlocked.Exchange(ref _wakePending, 0);
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}
		}

		private void ProcessQueue()
		{
			Interlocked.Exchange(ref _wakePending, 0);

			var processed = 0;
			while (processed < MaxItemsPerWakeUp && _queue.TryDequeue(out var item))
			{
				processed++;
				Execute(item.Callback, item.State);
			}

			if (!_queue.IsEmpty)
			{
				Wake();
			}
		}

		/// <summary>
		/// Runs a work item with this queue's synchronization context installed, so <c>await</c> continuations inside it
		/// resume on the owning thread.
		/// </summary>
		private void Execute(SendOrPostCallback callback, object? state)
		{
			var previous = System.Threading.SynchronizationContext.Current;
			try
			{
				System.Threading.SynchronizationContext.SetSynchronizationContext(SynchronizationContext);
				callback(state);
			}
			catch (Exception ex)
			{
				// Never let an exception escape into the native window procedure.
				ReportUnhandledException(ex);
			}
			finally
			{
				System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
			}
		}

		private void ReportUnhandledException(Exception exception)
		{
			try
			{
				var args = new BlazorWebViewUnhandledExceptionEventArgs(exception);
				UnhandledException?.Invoke(this, args);
				if (args.Handled)
				{
					return;
				}
			}
			catch (Exception handlerException)
			{
				exception = new AggregateException(exception, handlerException);
			}

			RethrowAsUnhandled(exception);
		}

		/// <summary>
		/// Exceptions can't be thrown across the native message loop, so surface them the same way an unhandled exception
		/// on any other thread is: it raises <see cref="AppDomain.UnhandledException"/> and terminates the process.
		/// </summary>
		internal static void RethrowAsUnhandled(Exception exception)
		{
			var edi = ExceptionDispatchInfo.Capture(exception);
			ThreadPool.UnsafeQueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi);
		}

		private static void EnsureWindowClassRegistered()
		{
			lock (ClassRegistrationLock)
			{
				if (_classRegistered)
				{
					return;
				}

				fixed (char* className = WindowClassName)
				{
					var wc = new WNDCLASSEXW
					{
						cbSize = (uint)sizeof(WNDCLASSEXW),
						lpfnWndProc = &WindowProc,
						hInstance = GetModuleHandleW(null),
						lpszClassName = className,
					};

					if (RegisterClassExW(&wc) == 0)
					{
						const int ERROR_CLASS_ALREADY_EXISTS = 1410;
						var error = Marshal.GetLastPInvokeError();
						if (error != ERROR_CLASS_ALREADY_EXISTS)
						{
							throw new Win32Exception(error);
						}
					}
				}

				_classRegistered = true;
			}
		}

		[UnmanagedCallersOnly]
		private static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
		{
			if (msg == WakeMessage)
			{
				var userData = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
				if (userData != 0 && GCHandle.FromIntPtr(userData).Target is Win32MessageQueue queue)
				{
					try
					{
						queue.ProcessQueue();
					}
					catch (Exception ex)
					{
						// Exceptions must not cross the native boundary; Execute already routes work item failures.
						RethrowAsUnhandled(ex);
					}
				}

				return 0;
			}

			return DefWindowProcW(hwnd, msg, wParam, lParam);
		}
	}
}
