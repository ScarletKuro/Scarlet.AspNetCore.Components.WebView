# Wpf.BlazorApp

A Blazor app hosted in a WPF `Window` by `Scarlet.AspNetCore.Components.WebView.Wpf`.

## Run

```powershell
dotnet run --project examples/Wpf.BlazorApp
```

Debug builds enable the WebView2 DevTools (F12).

## What it shows

| Page | Demonstrates |
| --- | --- |
| Home | Runtime info, and host → Blazor calls via `BlazorWebView.TryDispatchAsync` (counts window activations). |
| Counter | Rendering and event handling. |
| JS interop | .NET → JS with a source-generated `JsonSerializerContext`, JS → .NET with `[JSInvokable]`, and a component calling Win32 (`MessageBoxW`) through `[LibraryImport]`. |
| Links | `UrlLoading`: internal links stay in the view, external and `target="_blank"` links open in the browser, `file://` links are cancelled by the sample's policy. |
| Errors | `ErrorBoundary` for handler exceptions, `JSException` from JavaScript, and `Application.DispatcherUnhandledException` for everything else. |
| Background work | Extra windows whose component keeps working after the window is closed, then calls `InvokeAsync(StateHasChanged)` / `DispatchExceptionAsync` on the gone view ([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)). The app must keep running with no error dialog. |

The WPF `BlazorWebView` is not disposed with its window. `WindowExtensions.DisposeBlazorWebViewOnClose` closes the
window only after `BlazorWebView.DisposeAsync()` has finished, so Razor components are disposed while the WebView still
exists. The main window (`MainWindow.xaml`) and the background work windows both use it.

The pages are the same as in `examples/Win32Aot.BlazorApp` and `examples/WindowsForms.BlazorApp`, so the same UI test
(`tests/ui/Test-BlazorApp.ps1`) runs against all three.

The project consumes the library through a `ProjectReference`, so it imports the package's `build/` props and targets
explicitly. A NuGet consumer gets them automatically.
