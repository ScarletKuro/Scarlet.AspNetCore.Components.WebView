using System.Runtime.InteropServices;

namespace WindowsForms.BlazorApp.Services;

/// <summary>
/// Calls straight into Win32 from Razor components, using source-generated (AOT-safe) interop.
/// </summary>
public sealed partial class NativeDialogs
{
	private const uint MB_OK = 0x0;
	private const uint MB_ICONINFORMATION = 0x40;
	private const uint MB_ICONERROR = 0x10;

	public void ShowInformation(string message) => MessageBoxW(0, message, "WindowsForms.BlazorApp", MB_OK | MB_ICONINFORMATION);

	public void ShowError(string message) => MessageBoxW(0, message, "WindowsForms.BlazorApp - Error", MB_OK | MB_ICONERROR);

	[LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
