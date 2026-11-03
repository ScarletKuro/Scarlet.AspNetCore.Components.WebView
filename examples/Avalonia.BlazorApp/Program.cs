namespace Avalonia.BlazorApp;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);
}
