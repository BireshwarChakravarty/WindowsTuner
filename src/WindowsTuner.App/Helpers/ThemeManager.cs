using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.App.Helpers;

public static class ThemeManager
{
    public static readonly string[] AccentPresets =
        ["#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#06B6D4", "#64748B"];

    public static bool IsDark { get; private set; } = true;

    public static void Apply(AppSettings settings)
    {
        IsDark = settings.Theme switch
        {
            ThemeChoice.Light => false,
            ThemeChoice.System => !SystemUsesLightTheme(),
            _ => true,
        };

        var palette = IsDark
            ? new Dictionary<string, string>
            {
                ["BgBrush"] = "#0F1115", ["PanelBrush"] = "#151821", ["CardBrush"] = "#1C2030", ["CardHoverBrush"] = "#262B3D",
                ["LineBrush"] = "#2E3447", ["TextBrush"] = "#E8EAF0", ["SubTextBrush"] = "#9AA3B5",
            }
            : new Dictionary<string, string>
            {
                ["BgBrush"] = "#F3F4F7", ["PanelBrush"] = "#FAFBFC", ["CardBrush"] = "#FFFFFF", ["CardHoverBrush"] = "#EEF1F6",
                ["LineBrush"] = "#D9DDE5", ["TextBrush"] = "#1A1D24", ["SubTextBrush"] = "#5B6474",
            };

        var res = Application.Current.Resources;
        foreach (var (key, hex) in palette) res[key] = Brush(hex);

        var accent = ParseColor(settings.AccentColor) ?? (Color)ColorConverter.ConvertFromString("#3B82F6");
        res["AccentBrush"] = Freeze(new SolidColorBrush(accent));
        res["AccentSoftBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)));

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    public static Color? ParseColor(string? hex)
    {
        try { return hex is null ? null : (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }

    private static SolidColorBrush Brush(string hex) =>
        Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static bool SystemUsesLightTheme() =>
        WindowsRegistry.Instance.GetValue(RegHive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme") is int v && v == 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark/light native title bar (Windows 10 20H1+ / 11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); } catch { /* older Windows */ }
    }
}
