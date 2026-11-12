namespace VsCodeTabs;

internal readonly record struct TabLabel(string Name, string Detail);

internal static class TitleParser
{
    public static TabLabel Label(string title)
    {
        var parts = Parts(title);
        var path = FolderPath(parts);
        if (path is not null)
            return new TabLabel(Path.GetFileName(path), Path.GetFileName(Path.GetDirectoryName(path) ?? ""));
        return new TabLabel(parts.Count > 0 ? parts[^1] : title, "");
    }

    public static string Identity(string title)
    {
        var parts = Parts(title);
        return FolderPath(parts) ?? (parts.Count > 0 ? parts[^1] : title);
    }

    static List<string> Parts(string title)
    {
        var parts = title.TrimStart('●', ' ').Split(" - ").ToList();
        if (parts.Count > 1 && parts[^1].StartsWith("Visual Studio Code", StringComparison.Ordinal))
            parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    static string? FolderPath(List<string> parts) =>
        parts.FirstOrDefault(IsAbsolutePath)?.TrimEnd('\\', '/');

    static bool IsAbsolutePath(string part) =>
        part.Length > 2 && char.IsAsciiLetter(part[0]) && part[1] == ':' && part[2] is '\\' or '/';
}
