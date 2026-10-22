# Scarlet.AspNetCore.Components.WebView.WindowsForms

Host Blazor (Razor components) inside a Windows Forms application with WebView2. Your UI is written in Razor and C#;
it runs locally in-process (no web server), and calls into .NET and Windows APIs directly.

This package is a **drop-in replacement for Microsoft's `Microsoft.AspNetCore.Components.WebView.WindowsForms`**. It
keeps the same namespaces and types (`BlazorWebView`, `RootComponent`, `AddWindowsFormsBlazorWebView()`), and adds fixes
and features that are not available in the official package.

## Why use it instead of the official package?

Microsoft's Windows Forms `BlazorWebView` ships as part of .NET MAUI and is only updated on MAUI's release schedule.
This package is a fork of that code, released independently, with these changes:

- **Safer teardown when a form closes while components are still working.** Background work that calls
  `InvokeAsync(StateHasChanged)` or `DispatchExceptionAsync` after its form was closed
  ([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)) no longer reaches a disposed WebView: navigation
  and messages after disposal are dropped, and the disposed-renderer race ("The renderer does not have a component with
  ID ...") is not rethrown on the UI thread.
- **Shared WebView2 environment.** Several `BlazorWebView`s can reuse one `CoreWebView2Environment` instead of each
  creating its own, which saves that work for every view after the first
  (see [Sharing a WebView2 environment](#sharing-a-webview2-environment)).

The lifetime scenarios from dotnet/maui#34855 run in CI against real forms: closing, disposing, modal forms that are
never disposed, and rapid open/close.

## Getting started

Requirements: .NET 9 or later on Windows, and the
[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on Windows 11).

1. Use the Razor SDK and reference the package:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk.Razor">
     <PropertyGroup>
       <OutputType>WinExe</OutputType>
       <TargetFramework>net9.0-windows</TargetFramework>
       <UseWindowsForms>true</UseWindowsForms>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Scarlet.AspNetCore.Components.WebView.WindowsForms" Version="x.y.z" />
     </ItemGroup>
   </Project>
   ```

2. Add `wwwroot/index.html` with a `<div id="app">` and `<script src="_framework/blazor.webview.js"></script>`, and
   your Razor components (for example a `Routes.razor` with a `Router`).

3. Register the services and add a `BlazorWebView` to a form:

   ```csharp
   var services = new ServiceCollection();
   services.AddWindowsFormsBlazorWebView();
   #if DEBUG
   services.AddBlazorWebViewDeveloperTools();
   #endif

   var blazorWebView = new BlazorWebView
   {
       Dock = DockStyle.Fill,
       HostPage = @"wwwroot\index.html",
       Services = services.BuildServiceProvider(),
   };
   blazorWebView.RootComponents.Add<Routes>("#app");
   form.Controls.Add(blazorWebView);
   ```

A form shown with `Show()` is disposed when it closes, and disposes the `BlazorWebView` and its Razor components with
it. A form shown with `ShowDialog()` is only hidden when it closes; dispose it yourself.

## Migrating from Microsoft.AspNetCore.Components.WebView.WindowsForms

Replace the package reference. No code changes are needed. Don't reference both packages: they define the same types.

## Sharing a WebView2 environment

By default every `BlazorWebView` creates its own `CoreWebView2Environment`. Creating one is asynchronous and part of
the time before a view shows anything. If your app opens several views, or opens and closes them often, create the
environment once and share it: later views only create their WebView2 controller, and every view uses the same user
data folder and options.

The short form takes a factory, which runs once:

```csharp
services.AddWindowsFormsBlazorWebView()
    .UseSharedCoreWebView2Environment((sp, _) => new ValueTask<CoreWebView2Environment>(
        CoreWebView2Environment.CreateAsync(userDataFolder: @"C:\MyApp\WebView2")));
```

For full control, implement `ICoreWebView2EnvironmentProvider`:

```csharp
public sealed class SharedWebView2Environment : ICoreWebView2EnvironmentProvider
{
    // Created on first use. Concurrent first callers share the same task, and a failure is cached for all of them.
    private readonly Lazy<Task<CoreWebView2Environment>> _environment = new(CreateAsync);

    public ValueTask<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken = default)
        => new(_environment.Value);

    private static Task<CoreWebView2Environment> CreateAsync()
    {
        // A persistent user data folder keeps the HTTP cache and WebView2 state across app restarts.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: new CoreWebView2EnvironmentOptions("--no-proxy-server")
            {
                AreBrowserExtensionsEnabled = false,
                ExclusiveUserDataFolderAccess = true,
            });
    }
}
```

```csharp
services.AddWindowsFormsBlazorWebView()
    .UseSharedCoreWebView2Environment<SharedWebView2Environment>();
```

The options are only an example: `--no-proxy-server` makes WebView2 connect directly instead of resolving the system
proxy, which suits an app that loads only its own local content, and `ExclusiveUserDataFolderAccess` keeps other
processes out of the folder. There is also an overload that takes an already-created environment. You own the
environment's lifetime; the views never dispose a shared environment. A single view can still use its own environment
through `BlazorWebViewInitializingEventArgs.CoreWebView2Environment`, which takes precedence.

## More

- Example app: [examples/WindowsForms.BlazorApp](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/tree/master/examples/WindowsForms.BlazorApp)
- Other hosts: [Scarlet.AspNetCore.Components.WebView.Wpf](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Wpf)
  and the NativeAOT-friendly [Scarlet.AspNetCore.Components.WebView.Win32Aot](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Win32Aot)
- Source and issues: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
