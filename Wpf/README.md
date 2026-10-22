# Scarlet.AspNetCore.Components.WebView.Wpf

Host Blazor (Razor components) inside a WPF application with WebView2. Your UI is written in Razor and C#; it runs
locally in-process (no web server), and calls into .NET and Windows APIs directly.

This package is a **drop-in replacement for Microsoft's `Microsoft.AspNetCore.Components.WebView.Wpf`**. It keeps the
same namespaces, types and XAML (`BlazorWebView`, `RootComponent`, `AddWpfBlazorWebView()`), and adds fixes and
features that are not available in the official package.

## Why use it instead of the official package?

Microsoft's WPF `BlazorWebView` ships as part of .NET MAUI and is only updated on MAUI's release schedule. This package
is a fork of that code, released independently, with these changes:

- **No crash when a window closes while components are still working.** Background work that calls
  `InvokeAsync(StateHasChanged)` or `DispatchExceptionAsync` after its window was closed could take down the app with
  "WebView2 control is disposed" or "The renderer does not have a component with ID ..."
  ([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)). Messages to a disposed WebView are dropped, and
  the disposed-renderer race is no longer rethrown on the UI thread.
- **`DisposeAsync` stays on the UI thread** when it disposes the WebView2 control, which avoids the
  `InvalidCastException` for `ICoreWebView2Controller` ([dotnet/maui#26746](https://github.com/dotnet/maui/issues/26746)).
- **Bindable `RootComponent`.** `Selector`, `ComponentType` and `Parameters` are dependency properties, and root
  components are part of the logical tree, so they can bind to the `BlazorWebView`'s `DataContext` in XAML.
- **Classic `WebView2` control.** The view hosts the standard `WebView2` control rather than
  `WebView2CompositionControl`.
- **Shared WebView2 environment.** Several `BlazorWebView`s can reuse one `CoreWebView2Environment`
  (see [Sharing a WebView2 environment](#sharing-a-webview2-environment)).

The lifetime scenarios from dotnet/maui#34855 run in CI against real windows. With the same tests, Microsoft's package
fails the scenarios that call `DisposeAsync`.

## Getting started

Requirements: .NET 9 or later on Windows, and the
[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on Windows 11).

1. Use the Razor SDK and reference the package:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk.Razor">
     <PropertyGroup>
       <OutputType>WinExe</OutputType>
       <TargetFramework>net9.0-windows</TargetFramework>
       <UseWPF>true</UseWPF>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Scarlet.AspNetCore.Components.WebView.Wpf" Version="x.y.z" />
     </ItemGroup>
   </Project>
   ```

2. Add `wwwroot/index.html` with a `<div id="app">` and `<script src="_framework/blazor.webview.js"></script>`, and
   your Razor components (for example a `Routes.razor` with a `Router`).

3. Register the services (here in the window's constructor, before `InitializeComponent()`) and place a
   `BlazorWebView` in the window:

   ```csharp
   var services = new ServiceCollection();
   services.AddWpfBlazorWebView();
   #if DEBUG
   services.AddBlazorWebViewDeveloperTools();
   #endif
   Resources.Add("services", services.BuildServiceProvider());
   ```

   ```xml
   <Window x:Class="MyApp.MainWindow"
           xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           xmlns:blazor="http://schemas.microsoft.com/winfx/2006/xaml/presentation/blazor"
           xmlns:local="clr-namespace:MyApp">
       <blazor:BlazorWebView x:Name="blazorWebView" HostPage="wwwroot\index.html" Services="{DynamicResource services}">
           <blazor:BlazorWebView.RootComponents>
               <blazor:RootComponent Selector="#app" ComponentType="{x:Type local:Routes}" />
           </blazor:BlazorWebView.RootComponents>
       </blazor:BlazorWebView>
   </Window>
   ```

4. Dispose the view when its window closes. The WPF `BlazorWebView` is not disposed with its window; disposing it
   first lets Razor components run their own disposal while the WebView still exists:

   ```csharp
   var disposed = false;
   Closing += async (_, e) =>
   {
       if (disposed) return;
       e.Cancel = true;
       disposed = true;
       await blazorWebView.DisposeAsync();
       _ = Dispatcher.BeginInvoke(Close);
   };
   ```

## Migrating from Microsoft.AspNetCore.Components.WebView.Wpf

Replace the package reference. No code or XAML changes are needed. Don't reference both packages: they define the same
types.

## Sharing a WebView2 environment

By default each `BlazorWebView` creates its own `CoreWebView2Environment`. To share one (same browser process, same
user data folder, same options):

```csharp
services.AddWpfBlazorWebView()
    .UseSharedCoreWebView2Environment((sp, _) => new ValueTask<CoreWebView2Environment>(
        CoreWebView2Environment.CreateAsync(userDataFolder: @"C:\MyApp\WebView2")));
```

There are overloads that take an existing environment or a custom `ICoreWebView2EnvironmentProvider`. You own the
environment's lifetime. A single view can still use its own environment through
`BlazorWebViewInitializingEventArgs.CoreWebView2Environment`, which takes precedence.

## More

- Example app: [examples/Wpf.BlazorApp](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/tree/master/examples/Wpf.BlazorApp)
- Other hosts: [Scarlet.AspNetCore.Components.WebView.WindowsForms](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.WindowsForms)
  and the NativeAOT-friendly [Scarlet.AspNetCore.Components.WebView.Win32Aot](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Win32Aot)
- Source and issues: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
