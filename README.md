# Scarlet.AspNetCore.Components.WebView

[![Build](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/actions/workflows/build.yml/badge.svg)](https://github.com/ScarletKuro/Scarlet.AspNetCore.Components.WebView/actions/workflows/build.yml)

Blazor Hybrid hosts for Windows desktop apps. Write your UI as Razor components, run it locally inside WebView2 (no
web server), and call .NET and Windows APIs directly from it.

| Package | Host | Target | NuGet |
| --- | --- | --- | --- |
| [Scarlet.AspNetCore.Components.WebView.WindowsForms](WindowsForms/README.md) | Windows Forms `Form` | .NET 9+ | [![NuGet](https://img.shields.io/nuget/v/Scarlet.AspNetCore.Components.WebView.WindowsForms.svg)](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.WindowsForms) |
| [Scarlet.AspNetCore.Components.WebView.Wpf](Wpf/README.md) | WPF `Window` | .NET 9+ | [![NuGet](https://img.shields.io/nuget/v/Scarlet.AspNetCore.Components.WebView.Wpf.svg)](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Wpf) |
| [Scarlet.AspNetCore.Components.WebView.Win32Aot](Win32Aot/README.md) | Plain Win32 window, NativeAOT | .NET 10+ | [![NuGet](https://img.shields.io/nuget/v/Scarlet.AspNetCore.Components.WebView.Win32Aot.svg)](https://www.nuget.org/packages/Scarlet.AspNetCore.Components.WebView.Win32Aot) |

## Why does this exist?

Microsoft ships Blazor Hybrid hosts for Windows Forms and WPF (`Microsoft.AspNetCore.Components.WebView.WindowsForms`
and `.Wpf`) as part of .NET MAUI. They work, but fixes only arrive on MAUI's release schedule, and some long-standing
problems are still open. This repository forks those hosts and releases them independently:

- **Drop-in replacements.** The WindowsForms and Wpf packages keep Microsoft's namespaces, types and XAML. Swap the
  package reference; no code changes.
- **Lifetime fixes.** Closing a window while components still run background work no longer crashes the app
  ([dotnet/maui#34855](https://github.com/dotnet/maui/issues/34855)), and WPF's `DisposeAsync` disposes the WebView on
  the UI thread ([dotnet/maui#26746](https://github.com/dotnet/maui/issues/26746)).
- **WPF without blank screens.** The Wpf package keeps the standard `WebView2` control instead of the
  `WebView2CompositionControl` that Microsoft's WPF host uses since .NET 10. The composition control can leave the
  window blank on startup ([MicrosoftEdge/WebView2Feedback#5566](https://github.com/MicrosoftEdge/WebView2Feedback/issues/5566)).
- **Features.** Shared WebView2 environments for all views, and bindable WPF `RootComponent`s that pick up the
  `DataContext`.
- **A NativeAOT host.** The WinForms and WPF hosts depend on `Microsoft.Web.WebView2`, whose built-in COM interop can
  never work with NativeAOT or trimming. The Win32Aot package hosts Blazor on
  [WebView2Aot](https://github.com/smourier/WebView2Aot) instead, so a Blazor app can be published as a single ~10 MB
  native executable.

## Which package should I use?

- **You have a Windows Forms or WPF app**, or use Microsoft's package today: use the matching WindowsForms or Wpf
  package. See what changed compared to Microsoft's package in the [WPF](Wpf/README.md) and
  [Windows Forms](WindowsForms/README.md) READMEs.
- **You are starting a Blazor desktop app and want NativeAOT, fast startup or no UI framework**: use
  [Win32Aot](Win32Aot/README.md). It can also host Blazor in a window you already own (any `HWND`).

## Examples

Each host has an example app with the same pages (counter, JS interop, link handling, error handling, and windows that
close while their components keep working):

- [examples/WindowsForms.BlazorApp](examples/WindowsForms.BlazorApp)
- [examples/Wpf.BlazorApp](examples/Wpf.BlazorApp)
- [examples/Win32Aot.BlazorApp](examples/Win32Aot.BlazorApp) (published with NativeAOT)

```powershell
dotnet run --project examples/Wpf.BlazorApp
```

## How it is tested

CI runs, for each host, on a Windows runner with real windows:

- `tests/<Host>.LifetimeTests`: windows closed, destroyed or never disposed while a component keeps working in the
  background and then calls `InvokeAsync`/`DispatchExceptionAsync` (dotnet/maui#34855). No exception may reach the
  app's unhandled exception handler. Win32Aot runs them both with the JIT and as a NativeAOT executable.
- `tests/ui/Test-BlazorApp.ps1`: drives the published example through UI Automation (pages, clicks, JS interop, the
  background work page, clean exit).

## Building

```powershell
dotnet build WebView.slnx
```

Requires the .NET 10 SDK, and the .NET 9 runtime to run the WindowsForms and Wpf examples and tests. The apps run on
Windows only; to build on Linux, pass `/p:EnableWindowsTargeting=true` (CI does). NativeAOT publishing needs the Visual
Studio C++ build tools.

## License

Apache License 2.0. The WindowsForms and Wpf hosts are derived from
[dotnet/maui](https://github.com/dotnet/maui) (© .NET Foundation and contributors).
