using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Avalonia.BlazorApp.Services;

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
            services.AddSingleton<HostEvents>();
            services.AddSingleton<NativeDialogs>();
            services.AddSingleton<BackgroundWorkWindows>();
            var provider = services.BuildServiceProvider();
            desktop.Exit += (_, _) => provider.Dispose();
            desktop.MainWindow = new MainWindow(provider);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
