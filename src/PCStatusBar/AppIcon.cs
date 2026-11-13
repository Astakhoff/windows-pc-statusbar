namespace PCStatusBar;

internal static class AppIcon
{
    static readonly Lazy<Icon> icon = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PCStatusBar.App.ico")!;
        using var source = new Icon(stream, 32, 32);
        return (Icon)source.Clone();
    });
    public static Icon Image => icon.Value;
}
