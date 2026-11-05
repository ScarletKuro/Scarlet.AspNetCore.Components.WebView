# Avalonia.BlazorApp

A Windows Avalonia application embedding `Scarlet.AspNetCore.Components.WebView.Win32Aot` in a
`NativeControlHost`. Avalonia owns the UI thread, synchronization context and message loop. The example
does not create a DirectN application or message queue.

## Run

Requires Windows, the .NET 10 SDK and the WebView2 Runtime.

```powershell
dotnet run --project examples/Avalonia.BlazorApp
```

The Blazor sidebar has the same test pages as the other example apps:

| Page | Demonstrates |
| --- | --- |
| Home | Runtime information and window activations pushed from Avalonia through `TryDispatchAsync`. |
| Counter | Rendering and event handling. |
| JS interop | .NET to JS with source-generated JSON metadata, JS callbacks to .NET, and a native Win32 message box. |
| Links | Internal navigation, external links, `target="_blank"`, and cancellation of `file://` navigation. |
| Errors | The layout's `ErrorBoundary`, recovery, and caught JavaScript errors. |
| Background work | Avalonia windows closed while their Razor components keep working and dispatch after disposal. |
| Blazor inside Avalonia | Shared state, native Avalonia controls, UI context and worker dispatch. |

The native Avalonia controls appear only on **Blazor inside Avalonia**. Other pages use the full window
width, and shared counter state survives navigation. The Avalonia status bar remains visible on every page.

On that page, use **Send +1 to Blazor** in Avalonia and **Send +1 to Avalonia** in the Razor component to update both
panes through a shared DI service. The Avalonia button uses `BlazorWebView.TryDispatchAsync` to access
Blazor's scoped services. **Update from a worker** dispatches from the thread pool, awaits a delay on
the UI thread, verifies that the continuation uses `AvaloniaSynchronizationContext`, then updates both panes.

## Integration

- `AvaloniaUiDispatcher` puts Avalonia's dispatcher behind Win32Aot's `DirectNDispatcher` contract, so Blazor's
  work shares Avalonia's queue, ordering and unhandled-exception handling. It queues work with `InvokeAsync`, reports
  work Avalonia drops at shutdown, and rethrows callback exceptions through `Post` so they reach
  `Dispatcher.UnhandledException`. The view keeps Blazor's dispatching rules on top: work dispatched on the UI thread
  runs inline, and dropped work faults its task instead of never completing.
- `BlazorViewHost` creates an Avalonia-owned child HWND and passes it, with the adapter, to
  `new BlazorWebView(child.Handle, new AvaloniaUiDispatcher(Dispatcher))`.
  Avalonia positions and sizes this child in its layout.
- `NativeWindowMessages` observes `WM_SIZE` and updates WebView2 bounds from `GetClientRect`: physical
  pixels, including DPI changes, rather than Avalonia's logical layout units. Child position and top-level
  window moves notify WebView2 so native popups stay positioned correctly. Focus can be forwarded into
  WebView2, and minimizing the main window hides the controller. It also paints the child's background, which
  Avalonia leaves unpainted, so it doesn't show black until WebView2 appears.
- `BlazorWindow` provides the common window lifetime for the main window and background-work windows:
  it cancels its first close, awaits `BlazorViewHost.DisposeAsync`, then closes again. Its
  message loop and HWND remain alive while asynchronous Razor cleanup finishes. Pages unsubscribe from shared
  services when navigated away. JS interop is used after rendering, not during component disposal: Win32Aot
  disconnects the IPC bridge before disposing components.
- Failures of dispatched work reach `BlazorWebView.UnhandledException`. The sample handles this event, displays
  the error in Avalonia's status area, and sets `Handled`. Returned tasks still fault for awaiting callers. Errors
  left unhandled are rethrown through the adapter into Avalonia's `Dispatcher.UnhandledException`. Callback failures
  racing with view disposal are not promoted to the status area.

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

## Tests

Like the other examples, the published app is driven through UI Automation by
[`tests/ui/Test-BlazorApp.ps1`](../../tests/ui/Test-BlazorApp.ps1):

```powershell
powershell -File tests/ui/Test-BlazorApp.ps1 -Exe artifacts/avalonia-example/Avalonia.BlazorApp.exe -HomeHeading 'Blazor in Avalonia' -ExpectNativeAot
```

[`tests/Avalonia.LifetimeTests`](../../tests/Avalonia.LifetimeTests) runs this example's `BlazorViewHost` and
`BlazorWindow` through the lifetime scenarios of the other hosts, plus resizing and the Avalonia dispatcher.
