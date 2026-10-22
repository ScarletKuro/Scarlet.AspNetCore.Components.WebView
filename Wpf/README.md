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
  components are part of the logical tree, so they can bind to the `BlazorWebView`'s `DataContext` in XAML. This was
  proposed upstream in [dotnet/maui#34941](https://github.com/dotnet/maui/issues/34941) and closed as not planned (see
  [Binding root components in XAML](#binding-root-components-in-xaml)).
- **No blank screen on startup.** Since .NET 10, Microsoft's WPF `BlazorWebView` is built on
  `WebView2CompositionControl`, which can show a blank window instead of the page, and the window only recovers once
  the page changes or the control is moved or shown again
  ([MicrosoftEdge/WebView2Feedback#5566](https://github.com/MicrosoftEdge/WebView2Feedback/issues/5566)). This package
  keeps the standard `WebView2` control, which doesn't have this problem.
- **Shared WebView2 environment.** Several `BlazorWebView`s can reuse one `CoreWebView2Environment` instead of each
  creating its own, which saves that work for every view after the first
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

## Binding root components in XAML

`RootComponent` takes part in the `BlazorWebView`'s logical tree, so its properties bind against the inherited
`DataContext` like any other WPF element. No code-behind or binding proxy is needed:

```xml
<blazor:BlazorWebView HostPage="wwwroot\index.html" Services="{DynamicResource services}">
    <blazor:BlazorWebView.RootComponents>
        <blazor:RootComponent Selector="#app"
                              ComponentType="{x:Type local:Routes}"
                              Parameters="{Binding RootComponentParameters}" />
    </blazor:BlazorWebView.RootComponents>
</blazor:BlazorWebView>
```

```csharp
public sealed class MainViewModel
{
    public IDictionary<string, object?> RootComponentParameters { get; } = new Dictionary<string, object?>
    {
        ["Title"] = "Hello from the view model",
    };
}
```

When `Parameters` changes after the view has started (for example because the binding resolves late), the root
component is removed and added again with the new values, so its state starts over.

## Sharing a WebView2 environment

By default every `BlazorWebView` creates its own `CoreWebView2Environment`. Creating one is asynchronous and part of
the time before a view shows anything. If your app opens several views, or opens and closes them often, create the
environment once and share it: later views only create their WebView2 controller, and every view uses the same user
data folder and options.

The short form takes a factory, which runs once:

```csharp
services.AddWpfBlazorWebView()
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
services.AddWpfBlazorWebView()
    .UseSharedCoreWebView2Environment<SharedWebView2Environment>();
```

The options are only an example: `--no-proxy-server` makes WebView2 connect directly instead of resolving the system
proxy, which suits an app that loads only its own local content, and `ExclusiveUserDataFolderAccess` keeps other
processes out of the folder. There is also an overload that takes an
already-created environment. You own the environment's lifetime; the views never dispose a shared environment. A single
view can still use its own environment through `BlazorWebViewInitializingEventArgs.CoreWebView2Environment`, which
takes precedence.

## More

- Example app: [examples/Wpf.BlazorApp](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/tree/master/examples/Wpf.BlazorApp)
- Other hosts: [Scarlet.AspNetCore.Components.WebView.WindowsForms](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.WindowsForms)
  and the NativeAOT-friendly [Scarlet.AspNetCore.Components.WebView.Win32Aot](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Win32Aot)
- Source and issues: https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView
