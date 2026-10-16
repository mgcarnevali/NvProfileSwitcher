using Avalonia;
using Avalonia.Headless;
using NvProfileSwitcher.UiTests;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace NvProfileSwitcher.UiTests;

// Real App (resources and styles) rendered with Skia, so layout and screenshots match the app.
// On Windows the system font (Segoe UI) is used, as in the app. Other platforms have no
// system font in headless mode, so Inter is used there for previews only.
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UseSkia();
        if (!OperatingSystem.IsWindows())
            builder = builder.WithInterFont();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
