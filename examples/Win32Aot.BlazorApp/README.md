# Win32Aot.BlazorApp

A Blazor app hosted in a plain Win32 window by `Scarlet.AspNetCore.Components.WebView.Win32Aot`, published as a
NativeAOT executable.

## Run

```powershell
dotnet run --project examples/Win32Aot.BlazorApp
```

Debug builds enable the WebView2 DevTools (F12).

## Publish with NativeAOT

```powershell
dotnet publish examples/Win32Aot.BlazorApp -c Release -r win-x64   # or win-arm64
```

The output is `Win32Aot.BlazorApp.exe` (~10 MB, no .NET runtime needed), `WebView2Loader.dll` and `wwwroot/`. The
*Home* page shows `Native AOT: Yes` when it runs as a native executable.

Publishing prints trim/AOT warnings from Blazor's own assemblies. These are expected: the package's build defaults
(see the repository README) work around the ones that matter at runtime.

## What it shows

| Page | Demonstrates |
| --- | --- |
| Home | Runtime info, and host → Blazor calls via `BlazorWebView.TryDispatchAsync` (counts window activations). |
| Counter | Rendering and event handling. |
| JS interop | .NET → JS with a source-generated `JsonSerializerContext`, JS → .NET with `[JSInvokable]`, and a component calling Win32 (`MessageBoxW`) through `[LibraryImport]`. |
| Links | `UrlLoading`: internal links stay in the view, external and `target="_blank"` links open in the browser, `file://` links are cancelled by the sample's policy. |
| Errors | `ErrorBoundary` for handler exceptions, `JSException` from JavaScript, and `BlazorWebView.UnhandledException` for everything else. |

The project consumes the library through a `ProjectReference`, so it imports the package's `build/` props and targets
explicitly. A NuGet consumer gets them automatically.
