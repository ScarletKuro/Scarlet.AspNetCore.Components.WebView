# WindowsForms.BlazorApp

A Blazor app hosted in a Windows Forms `Form` by `Scarlet.AspNetCore.Components.WebView.WindowsForms`.

## Run

```powershell
dotnet run --project examples/WindowsForms.BlazorApp
```

Debug builds enable the WebView2 DevTools (F12).

## What it shows

| Page | Demonstrates |
| --- | --- |
| Home | Runtime info, and host → Blazor calls via `BlazorWebView.TryDispatchAsync` (counts form activations). |
| Counter | Rendering and event handling. |
| JS interop | .NET → JS with a source-generated `JsonSerializerContext`, JS → .NET with `[JSInvokable]`, and a component calling Win32 (`MessageBoxW`) through `[LibraryImport]`. |
| Links | `UrlLoading`: internal links stay in the view, external and `target="_blank"` links open in the browser, `file://` links are cancelled by the sample's policy. |
| Errors | `ErrorBoundary` for handler exceptions, `JSException` from JavaScript, and `Application.ThreadException` for everything else. |
| Background work | Extra forms whose component keeps working after the form is closed, then calls `InvokeAsync(StateHasChanged)` / `DispatchExceptionAsync` on the gone view ([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)). The app must keep running with no error dialog. |

A form shown with `Show()` is disposed when it closes, and disposes its `BlazorWebView` (and the Razor components)
with it. A form shown with `ShowDialog()` is only hidden; dispose it yourself.

The pages are the same as in `examples/Win32Aot.BlazorApp` and `examples/Wpf.BlazorApp`, so the same UI test
(`tests/ui/Test-BlazorApp.ps1`) runs against all three.

The project consumes the library through a `ProjectReference`, so it imports the package's `build/` props and targets
explicitly. A NuGet consumer gets them automatically.
