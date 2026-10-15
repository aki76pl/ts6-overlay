using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TS6Overlay;

// Opt-in rendering check for release validation; never used during normal startup.
internal static class DesignCheck
{
    internal static void Run(App app)
    {
        string output = Path.Combine(Config.Dir, "design-check");
        Directory.CreateDirectory(output);
        try
        {
            var settings = new SettingsWindow(app);
            settings.Show();
            for (int i = 0; i < 10; i++)
            {
                settings.SelectTab(i);
                settings.UpdateLayout();
                Save(settings, Path.Combine(output, $"settings-{i}.png"));
            }
            var overlay = new OverlayView(app.Cfg, preview: true);
            overlay.ApplyTheme(Theme.ByName("Aurora")); overlay.ShowDemo();
            overlay.Measure(new Size(600, 1000));
            overlay.Arrange(new Rect(overlay.DesiredSize)); overlay.UpdateLayout();
            Save(overlay, Path.Combine(output, "overlay.png"));
            settings.Close();
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: 10 settings sections and Aurora overlay rendered.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Shutdown(); }
    }

    static void Save(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
