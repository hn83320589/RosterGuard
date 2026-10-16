using Avalonia;
using Avalonia.Headless;
using RosterGuard.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RosterGuard.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
