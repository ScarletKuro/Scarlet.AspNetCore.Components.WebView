# Scarlet.AspNetCore.Components.WebView

NuGet packages for hosting Blazor content in Windows desktop applications with WebView2.

## Packages

- `Scarlet.AspNetCore.Components.WebView.WindowsForms`: Blazor in Windows Forms.
- `Scarlet.AspNetCore.Components.WebView.Wpf`: Blazor in WPF.
- `Scarlet.AspNetCore.Components.WebView.Win32Aot`: Blazor in a plain Win32 window, NativeAOT and trimming friendly.

## Win32Aot

`Scarlet.AspNetCore.Components.WebView.Win32Aot` hosts Blazor on top of [WebView2Aot](https://github.com/smourier/WebView2Aot).
WebView2Aot uses source-generated COM interop (`[GeneratedComInterface]`, `[LibraryImport]`), so there's no WinForms,
no WPF and no built-in COM. The host library itself is free of trim, AOT and single-file analyzer warnings, and the
build enforces it.

Requirements: .NET 10, Windows 10+, and the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/),
which is preinstalled on Windows 11. `WebView2Loader.dll` ships inside the package.

### Quick start

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Scarlet.AspNetCore.Components.WebView.Win32Aot" Version="x.y.z" />
  </ItemGroup>
</Project>
```

```csharp
[STAThread] // WebView2 requires an STA thread
static void Main()
{
    var services = new ServiceCollection();
    services.AddWin32AotBlazorWebView();
#if DEBUG
    services.AddBlazorWebViewDeveloperTools();
#endif
    using var serviceProvider = services.BuildServiceProvider();

    using var app = new DirectN.Extensions.Utilities.Application();
    using var window = new BlazorWebViewWindow("My app");
    window.BlazorWebView.HostPage = @"wwwroot\index.html";
    window.BlazorWebView.Services = serviceProvider;
    window.BlazorWebView.RootComponents.Add<Routes>("#app");
    window.Show();
    app.Run();
}
```

`BlazorWebViewWindow` is a DirectN `Window` that fills its client area with a `BlazorWebView`. It forwards resize, move
and focus to the view, starts it on creation, and disposes Razor components before the window is destroyed.

To host Blazor in any other window (a WinForms `Form.Handle`, a WPF `HwndSource`, a hand-written Win32 window), use
`BlazorWebView` directly. Create, use and dispose it on that window's UI thread:

```csharp
var view = new BlazorWebView(parentHwnd)
{
    HostPage = @"wwwroot\index.html",
    Services = serviceProvider,
};
view.RootComponents.Add<Routes>("#app");
await view.StartAsync();             // throws if WebView2 can't start (e.g. runtime missing)

view.Bounds = clientRect;            // on WM_SIZE
view.NotifyParentWindowPositionChanged(); // on WM_MOVE
view.MoveFocus();                    // on WM_SETFOCUS
await view.DisposeAsync();           // before destroying the parent window
```

The API mirrors the WinForms/WPF `BlazorWebView`: `UrlLoading`, `BlazorWebViewInitializing` (user data folder, browser
folder, environment options, or a pre-created environment), `BlazorWebViewInitialized`, `TryDispatchAsync`,
`UseSharedCoreWebView2Environment` and `AddBlazorWebViewDeveloperTools`. On top of that:

- `UnhandledException`: reports exceptions from work dispatched to the UI thread. Set `Handled` to keep the process
  alive; otherwise the exception is rethrown as a normal unhandled exception.
- `Bounds`, `IsVisible`, `MoveFocus`, `NotifyParentWindowPositionChanged`: control the WebView2 controller.
- `WebView` / `Controller`: the underlying WebView2Aot `ICoreWebView2` / `ICoreWebView2Controller` objects.

### NativeAOT and trimming

Blazor itself is not trim/AOT clean yet, mostly because its IPC and JS interop use reflection-based System.Text.Json.
The package's `build/` props and targets apply defaults that make a Blazor app work when it is published with
`PublishAot=true`. Every default can be overridden in your project file:

| Default | Why |
| --- | --- |
| `JsonSerializerIsReflectionEnabledByDefault=true` | Blazor WebView IPC and JS interop serialize through reflection (same default as the Blazor WebAssembly SDK). |
| `TrimMode=partial` | Razor components are created and get parameters through reflection, and Razor-generated code passes component types (layouts, `NotFoundPage`, ...) in ways the trimmer can't follow. Only assemblies marked `IsTrimmable` (the framework) are trimmed. |
| DirectN / DirectN.Extensions trimmed anyway | They are built with `IsAotCompatible` but `GenerateAssemblyInfo=false`, so the `IsTrimmable` attribute is missing from the binaries. Without this, partial mode would keep tens of MB of DirectX interop. Opt out with `ScarletBlazorWebViewKeepDirectN=true`. |
| `rd.xml` runtime directives | Roots the generic System.Text.Json converters (over `JsonElement` and Blazor's enums) that Blazor's IPC needs at runtime. Opt out with `ScarletBlazorWebViewDisableRdXml=true`. |

The example publishes to a single ~10 MB native executable plus `WebView2Loader.dll` and `wwwroot`.

What remains your responsibility, as in any AOT app:

- **Your own JSON types.** Pass them to and from JavaScript as JSON strings, and (de)serialize with a source-generated
  `JsonSerializerContext` (see `examples/Win32Aot.BlazorApp/Components/Pages/JsInterop.razor`). Reflection-based serialization of
  value-type collections and generics (e.g. `InvokeAsync<int[]>`, `[JSInvokable]` methods returning `Task<int>`)
  can fail under AOT. The fix is to avoid them or root them in your own rd.xml.
- **Expected publish warnings.** Blazor's own `IL2xxx`/`IL3xxx` warnings show up at publish time. Watch for new
  warnings from your code.

### Troubleshooting

- **`'vswhere.exe' is not recognized` / `link.exe ... exited with code 3` during AOT publish.** Some Visual Studio
  installations' `vcvarsall.bat` expect `vswhere.exe` on `PATH`. Add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`
  to `PATH`.
- **Startup fails with a WebView2 error.** Install the WebView2 Runtime, or set
  `BlazorWebViewInitializingEventArgs.BrowserExecutableFolder` to a fixed-version runtime.
- **Two copies of `WebView2Loader.dll`.** RID-less builds place it under `runtimes\win-<arch>\native\`, and RID-specific
  publishes put it next to the exe. Both locations are probed.

## Repository

Source and issue tracking: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
