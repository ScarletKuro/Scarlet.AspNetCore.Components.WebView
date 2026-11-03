# Avalonia.BlazorApp

A Windows Avalonia application embedding `Scarlet.AspNetCore.Components.WebView.Win32Aot` in a
`NativeControlHost`. Avalonia owns the UI thread, synchronization context and message loop. The example
does not create a DirectN application or message queue.

## Run

Requires Windows, the .NET 10 SDK and the WebView2 Runtime.

```powershell
dotnet run --project examples/Avalonia.BlazorApp
```

Use **Send +1 to Blazor** in Avalonia and **Send +1 to Avalonia** in the Razor component to update both
panes through a shared DI service. The Avalonia button uses `BlazorWebView.TryDispatchAsync` to access
Blazor's scoped services. **Update from a worker** dispatches from the thread pool, awaits a delay on
the UI thread, verifies that the continuation uses `AvaloniaSynchronizationContext`, then updates both panes.

## Integration

- `AvaloniaBlazorWebViewThreading` implements the host's public `IBlazorWebViewThreading` contract. It
  supplies an Avalonia synchronization context, delegates `CheckAccess` to its dispatcher, and reports
  failures through the integration's exception event. `AvaloniaBlazorDispatcher` adapts Avalonia's
  dispatcher to Blazor's existing dispatcher abstraction, including all four `InvokeAsync` overloads,
  asynchronous results and exceptions.
- `BlazorViewHost` creates an Avalonia-owned child HWND and passes it, with the adapter, to
  `new BlazorWebView(child.Handle, new AvaloniaBlazorWebViewThreading(Dispatcher))`.
  Avalonia positions and sizes this child in its layout.
- `NativeWindowMessages` observes `WM_SIZE` and updates WebView2 bounds from `GetClientRect`: physical
  pixels, including DPI changes, rather than Avalonia's logical layout units. Child position and top-level
  window moves notify WebView2 so native popups stay positioned correctly. Focus can be forwarded into
  WebView2, and minimizing the main window hides the controller.
- `MainWindow` cancels its first close, awaits `BlazorViewHost.DisposeAsync`, then closes again. Its
  message loop and HWND remain alive while asynchronous Razor cleanup finishes. The component unsubscribes
  from the shared service and performs asynchronous cleanup. JS interop is used after rendering, not
  during component disposal: Win32Aot disconnects the IPC bridge before disposing components.
- The view forwards integration failures to `BlazorWebView.UnhandledException`. The sample handles
  this event, displays the error in Avalonia's status area, and sets `Handled`. Returned tasks still
  fault for awaiting callers. Errors left unhandled are posted to Avalonia's dispatcher exception path.
  Callback failures racing with view disposal are not promoted to the status area.
- Each view owns one threading integration and disposes it after WebView cleanup. Integration disposal
  detaches the view's callbacks; Avalonia's dispatcher, context and application loop stay alive. Other
  views and already dispatched work can continue using the shared UI thread.

The sample host has one lifetime: dispose it before permanently removing it from the visual tree, then
create a new instance to attach again. Its synchronous native destruction callback also initiates
cleanup on unexpected detach, but orderly window close explicitly awaits disposal first.

This uses windowed WebView2. Avalonia controls cannot overlay its HWND, and arbitrary Avalonia transforms
do not apply to its content. This sample does not implement complete keyboard tab traversal between
WebView2 and Avalonia; use the **Focus Blazor** button or click the desired pane.

## NativeAOT

```powershell
dotnet publish examples/Avalonia.BlazorApp -c Release -r win-x64 -o artifacts/avalonia-example
```

NativeAOT publishing requires the Visual Studio C++ build tools. As with the plain Win32Aot example,
Blazor's dependencies can produce trim/AOT warnings. The project explicitly imports the Win32Aot package's
build props and targets because it consumes the host through a project reference; NuGet consumers receive
these imports automatically.

## Integration smoke test

```powershell
dotnet run --project examples/Avalonia.BlazorApp -c Release -- --smoke-test
# Or test the actual published native executable:
./artifacts/avalonia-example/Avalonia.BlazorApp.exe --smoke-test
```

The opt-in test opens a real window and checks Razor/JS startup, the synchronization context, both
directions of communication, worker dispatch, all dispatcher overloads and asynchronous failures,
the view's exception event and Avalonia's unhandled-exception policy, resize in both directions,
browser viewport size, and awaited asynchronous component disposal.
It exits with code 0 on success or 1 on failure and writes `avalonia-smoke-test.log` beside the executable.
Use `--smoke-test-output <path>` to choose another report location (its directory must exist).
