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
- **Shared WebView2 environment.** Several `BlazorWebView`s can reuse one `CoreWebView2Environment`
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

By default each `BlazorWebView` creates its own `CoreWebView2Environment`. To share one (same browser process, same
user data folder, same options):

```csharp
services.AddWindowsFormsBlazorWebView()
    .UseSharedCoreWebView2Environment((sp, _) => new ValueTask<CoreWebView2Environment>(
        CoreWebView2Environment.CreateAsync(userDataFolder: @"C:\MyApp\WebView2")));
```

There are overloads that take an existing environment or a custom `ICoreWebView2EnvironmentProvider`. You own the
environment's lifetime. A single view can still use its own environment through
`BlazorWebViewInitializingEventArgs.CoreWebView2Environment`, which takes precedence.

## More

- Example app: [examples/WindowsForms.BlazorApp](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/tree/master/examples/WindowsForms.BlazorApp)
- Other hosts: [Scarlet.AspNetCore.Components.WebView.Wpf](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Wpf)
  and the NativeAOT-friendly [Scarlet.AspNetCore.Components.WebView.Win32Aot](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Win32Aot)
- Source and issues: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
