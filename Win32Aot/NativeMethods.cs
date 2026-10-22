using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	/// <summary>
	/// Minimal, source-generated (AOT-safe) Win32 interop used by <see cref="Win32Dispatcher"/>.
	/// </summary>
	internal static unsafe partial class NativeMethods
	{
		public const int GWLP_USERDATA = -21;
		public static readonly nint HWND_MESSAGE = -3;

		[StructLayout(LayoutKind.Sequential)]
		public struct WNDCLASSEXW
		{
			public uint cbSize;
			public uint style;
			public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
			public int cbClsExtra;
			public int cbWndExtra;
			public nint hInstance;
			public nint hIcon;
			public nint hCursor;
			public nint hbrBackground;
			public char* lpszMenuName;
			public char* lpszClassName;
			public nint hIconSm;
		}

		[LibraryImport("user32.dll", SetLastError = true)]
		public static partial ushort RegisterClassExW(WNDCLASSEXW* lpwcx);

		[LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		public static partial nint CreateWindowExW(
			uint dwExStyle,
			string lpClassName,
			string? lpWindowName,
			uint dwStyle,
			int x,
			int y,
			int nWidth,
			int nHeight,
			nint hWndParent,
			nint hMenu,
			nint hInstance,
			nint lpParam);

		[LibraryImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool DestroyWindow(nint hWnd);

		[LibraryImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

		[LibraryImport("user32.dll")]
		public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

		[LibraryImport("user32.dll", SetLastError = true)]
		public static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

		[LibraryImport("user32.dll", SetLastError = true)]
		public static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

		[LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		public static partial uint RegisterWindowMessageW(string lpString);

		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		public static partial nint GetModuleHandleW(string? lpModuleName);
	}
}
