using System.IO;

namespace TodoWidget.Services;

/// <summary>
/// Resolves where <c>data.json</c> and <c>settings.json</c> live.
/// The app is portable by default: the files sit next to the executable.
/// If that folder is read-only (e.g. installed under Program Files) it falls
/// back to %APPDATA%\TodoWidget so the widget never loses data.
/// </summary>
public static class AppPaths
{
    private static string? _baseDir;

    public static string BaseDir => _baseDir ??= ResolveBaseDir();

    public static string DataFile => Path.Combine(BaseDir, "data.json");

    public static string SettingsFile => Path.Combine(BaseDir, "settings.json");

    private static string ResolveBaseDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (IsWritable(exeDir)) return exeDir;

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoWidget");
        Directory.CreateDirectory(roaming);
        return roaming;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".todo-write-probe");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
                // The handle closes here, which also deletes the probe file.
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
