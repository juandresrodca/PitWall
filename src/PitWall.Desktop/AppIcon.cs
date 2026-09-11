namespace PitWall.Desktop;

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Load()
    {
        if (_icon is not null)
        {
            return _icon;
        }
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PitWall.Desktop.pitwall.ico");
        _icon = stream is null ? SystemIcons.Application : new Icon(stream);
        return _icon;
    }
}
