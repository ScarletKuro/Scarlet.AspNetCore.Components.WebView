# Scarlet.AspNetCore.Components.WebView.Win32Aot

Host Blazor (Razor components) in a plain Win32 window with WebView2, **without WinForms or WPF**, and publish the app
with **NativeAOT** as a single native executable (about 10 MB, no .NET runtime to install).

## Why?

Blazor Hybrid on Windows normally means Microsoft's WinForms or WPF `BlazorWebView`. Both sit on top of the
`Microsoft.Web.WebView2` package, which uses built-in COM interop. That doesn't work with NativeAOT or trimming, and
never will. Blazor itself is getting closer to being AOT-clean, but the WebView host stays the blocker.

This package replaces that host. It is built on [WebView2Aot](https://github.com/smourier/WebView2Aot), which talks to
WebView2 through source-generated COM interop (`[GeneratedComInterface]`, `[LibraryImport]`), and
[DirectN](https://github.com/smourier/DirectNAot) for the window. Use it when you want:

- **NativeAOT**: fast startup, a single native exe, no runtime to deploy.
- **No UI framework**: a small Win32 host, or Blazor inside a window you already own (any `HWND`).
- **A host that is warning-free under the trim, AOT and single-file analyzers.** The build enforces it, so the host is
  ready for the day Blazor is fully AOT-clean.

Requirements: .NET 10, Windows 10+, and the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/),
which is preinstalled on Windows 11. `WebView2Loader.dll` ships inside the package.

## Quick start

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

Add `wwwroot/index.html` with a `<div id="app">` and `<script src="_framework/blazor.webview.js"></script>`, and your
Razor components. Publish with `dotnet publish -c Release -r win-x64`.

`BlazorWebViewWindow` is a DirectN `Window` that fills its client area with a `BlazorWebView`. It forwards resize, move
and focus to the view, starts it on creation, and disposes Razor components before the window is destroyed.

## Hosting in your own window

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

## API

The API mirrors the WinForms/WPF `BlazorWebView`: `UrlLoading`, `BlazorWebViewInitializing` (user data folder, browser
folder, environment options, or a pre-created environment), `BlazorWebViewInitialized`, `TryDispatchAsync`,
`UseSharedCoreWebView2Environment` (see [Sharing a WebView2 environment](#sharing-a-webview2-environment)) and
`AddBlazorWebViewDeveloperTools`. On top of that:

- `UnhandledException`: reports exceptions from work dispatched to the UI thread. Set `Handled` to keep the process
  alive; otherwise the exception is rethrown as a normal unhandled exception.
- `Bounds`, `IsVisible`, `MoveFocus`, `NotifyParentWindowPositionChanged`: control the WebView2 controller.
- `WebView` / `Controller`: the underlying WebView2Aot `ICoreWebView2` / `ICoreWebView2Controller` objects.

Closing or destroying a window while components still run background work is safe: once the WebView is gone, further
navigations and messages are dropped instead of failing
([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)). CI checks these scenarios both with the JIT and as
a NativeAOT executable.

## Sharing a WebView2 environment

By default every `BlazorWebView` creates its own WebView2 environment. Creating one is asynchronous and part of the time
before a view shows anything. If your app opens several views, or opens and closes them often, create the environment
once and share it: later views only create their WebView2 controller, and every view uses the same user data folder and
options.

The short form takes a factory, which runs once:

```csharp
services.AddWin32AotBlazorWebView()
    .UseSharedCoreWebView2Environment(async (sp, _) =>
        await WebView2.Functions.CreateCoreWebView2EnvironmentWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: @"C:\MyApp\WebView2",
            environmentOptions: null)
        ?? throw new InvalidOperationException("WebView2 environment could not be created."));
```

For full control, implement `ICoreWebView2EnvironmentProvider`:

```csharp
public sealed class SharedWebView2Environment : ICoreWebView2EnvironmentProvider
{
    // Created on first use. Concurrent first callers share the same task, and a failure is cached for all of them.
    private readonly Lazy<Task<IComObject<ICoreWebView2Environment>>> _environment = new(CreateAsync);

    public ValueTask<IComObject<ICoreWebView2Environment>> GetEnvironmentAsync(CancellationToken cancellationToken = default)
        => new(_environment.Value);

    private static async Task<IComObject<ICoreWebView2Environment>> CreateAsync()
    {
        // A persistent user data folder keeps the HTTP cache and WebView2 state across app restarts.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        return await WebView2.Functions.CreateCoreWebView2EnvironmentWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                environmentOptions: null)
            ?? throw new InvalidOperationException("WebView2 environment could not be created.");
    }
}
```

```csharp
services.AddWin32AotBlazorWebView()
    .UseSharedCoreWebView2Environment<SharedWebView2Environment>();
```

Pass a `WebView2.CoreWebView2EnvironmentOptions` as `environmentOptions` to set browser arguments, the language, or
other environment options. There is also an overload that takes an already-created environment. You own the
environment's lifetime; the views never dispose a shared environment. A single view can still use its own environment
through `BlazorWebViewInitializingEventArgs.CoreWebView2Environment`, which takes precedence.

## NativeAOT and trimming

Blazor itself is not trim/AOT clean yet, mostly because its IPC and JS interop use reflection-based System.Text.Json.
The package's `build/` props and targets apply defaults that make a Blazor app work when it is published with
`PublishAot=true`. Every default can be overridden in your project file:

| Default | Why |
| --- | --- |
| `JsonSerializerIsReflectionEnabledByDefault=true` | Blazor WebView IPC and JS interop serialize through reflection (same default as the Blazor WebAssembly SDK). |
| `TrimMode=partial` | Razor components are created and get parameters through reflection, and Razor-generated code passes component types (layouts, `NotFoundPage`, ...) in ways the trimmer can't follow. Only assemblies marked `IsTrimmable` are trimmed: the framework, WebView2Aot and DirectN (the package requires DirectNAot 1.7.1+, the first version marked trimmable). |
| `rd.xml` runtime directives | Roots the generic System.Text.Json converters (over `JsonElement` and Blazor's enums) that Blazor's IPC needs at runtime. Opt out with `ScarletBlazorWebViewDisableRdXml=true`. |

What remains your responsibility, as in any AOT app:

- **Your own JSON types.** Pass them to and from JavaScript as JSON strings, and (de)serialize with a source-generated
  `JsonSerializerContext`. Reflection-based serialization of value-type collections and generics (e.g.
  `InvokeAsync<int[]>`, `[JSInvokable]` methods returning `Task<int>`) can fail under AOT. The fix is to avoid them or
  root them in your own rd.xml.
- **Expected publish warnings.** Blazor's own `IL2xxx`/`IL3xxx` warnings show up at publish time. Watch for new
  warnings from your code.

## Troubleshooting

- **`'vswhere.exe' is not recognized` / `link.exe ... exited with code 3` during AOT publish.** Some Visual Studio
  installations' `vcvarsall.bat` expect `vswhere.exe` on `PATH`. Add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`
  to `PATH`.
- **Startup fails with a WebView2 error.** Install the WebView2 Runtime, or set
  `BlazorWebViewInitializingEventArgs.BrowserExecutableFolder` to a fixed-version runtime.
- **Two copies of `WebView2Loader.dll`.** RID-less builds place it under `runtimes\win-<arch>\native\`, and RID-specific
  publishes put it next to the exe. Both locations are probed.

## More

- Example app: [examples/Win32Aot.BlazorApp](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/tree/master/examples/Win32Aot.BlazorApp)
  (shows the AOT-friendly JS interop pattern in `Components/Pages/JsInterop.razor`)
- Hosts for existing WinForms/WPF apps: [Scarlet.AspNetCore.Components.WebView.WindowsForms](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.WindowsForms)
  and [Scarlet.AspNetCore.Components.WebView.Wpf](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Wpf)
- Source and issues: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
