using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.AspNetCore.Components.WebView.Win32Aot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalonia.BlazorApp;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddDebug());
            services.AddWin32AotBlazorWebView();
#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif
            services.AddSingleton<SharedCounter>();
            var provider = services.BuildServiceProvider();
            desktop.Exit += (_, _) => provider.Dispose();
            var window = new MainWindow(provider);
            desktop.MainWindow = window;

            if (desktop.Args?.Contains("--smoke-test") == true)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window.Opened += (_, _) => _ = SmokeTest.RunAsync(window, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
