using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DirectN;

namespace Avalonia.BlazorApp;

// Observe the actual child HWND size (physical pixels), rather than guessing when Avalonia's layout
// will move/resize it. No DirectN Application or second message loop is created.
internal sealed unsafe partial class NativeWindowMessages : IDisposable
{
	internal const uint Size = 0x0005;
	internal const uint SetFocus = 0x0007;
	internal const uint WindowPositionChanged = 0x0047;
	private const uint NonClientDestroy = 0x0082;
	private const nuint SubclassId = 1;

	private readonly Action<uint> _onMessage;
	private readonly Action<Exception> _onError;
	private GCHandle _self;
	private nint _hwnd;

	public NativeWindowMessages(nint hwnd, Action<uint> onMessage, Action<Exception> onError)
	{
		_onMessage = onMessage;
		_onError = onError;
		_self = GCHandle.Alloc(this);
		_hwnd = hwnd;
		if (SetWindowSubclass(hwnd, &WindowProc, SubclassId, (nuint)GCHandle.ToIntPtr(_self)) == 0)
		{
			_self.Free();
			throw new Win32Exception("Could not subclass the native WebView host window.");
		}
	}

	public RECT ClientRect
	{
		get
		{
			if (!Functions.GetClientRect(new HWND { Value = _hwnd }, out var rect))
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}
			return rect;
		}
	}

	public void Dispose()
	{
		if (_hwnd != 0)
		{
			_ = RemoveWindowSubclass(_hwnd, &WindowProc, SubclassId);
			_hwnd = 0;
			_self.Free();
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint data)
	{
		var self = (NativeWindowMessages)GCHandle.FromIntPtr((nint)data).Target!;
		try
		{
			if (message == NonClientDestroy)
			{
				self.Dispose();
			}
			else
			{
				self._onMessage(message);
			}
		}
		catch (Exception ex)
		{
			// Managed exceptions must never cross the native window procedure boundary.
			self._onError(ex);
		}
		return DefSubclassProc(hwnd, message, wParam, lParam);
	}

	[LibraryImport("comctl32.dll")]
	private static partial int SetWindowSubclass(nint hwnd,
		delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

	[LibraryImport("comctl32.dll")]
	private static partial int RemoveWindowSubclass(nint hwnd,
		delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);

	[LibraryImport("comctl32.dll")]
	private static partial nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
