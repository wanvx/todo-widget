using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoWidget.Models;

namespace TodoWidget.Services;

/// <summary>
/// Tiny JSON-backed repository. Writes are atomic (temp file + replace) so a
/// crash mid-save can never truncate the user's list.
/// </summary>
public static class TaskStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<TodoTask> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.DataFile)) return new List<TodoTask>();

            var json = File.ReadAllText(AppPaths.DataFile);
            if (string.IsNullOrWhiteSpace(json)) return new List<TodoTask>();

            var tasks = JsonSerializer.Deserialize<List<TodoTask>>(json, Options) ?? new List<TodoTask>();

            // Defensive: drop entries that could not have come from a real user action.
            tasks.RemoveAll(t => string.IsNullOrWhiteSpace(t.Title) || t.Id is null);
            foreach (var t in tasks) t.DueAt = UrgencyRules.Normalize(t.DueAt);
            return tasks;
        }
        catch (Exception)
        {
            // A corrupt or unreadable list must not take the widget down; keep the
            // file around for inspection instead of silently overwriting it.
            TryBackupCorruptFile();
            return new List<TodoTask>();
        }
    }

    public static void Save(IEnumerable<TodoTask> tasks)
    {
        try
        {
            var json = JsonSerializer.Serialize(tasks.ToList(), Options);
            WriteAtomic(AppPaths.DataFile, json);
        }
        catch (Exception)
        {
            // Persistence failure must never take the widget down; the in-memory
            // list stays authoritative.
        }
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception)
        {
            // See Save().
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);

        if (File.Exists(path))
        {
            File.Replace(temp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            if (!File.Exists(AppPaths.DataFile)) return;
            var backup = AppPaths.DataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(AppPaths.DataFile, backup, overwrite: true);
        }
        catch
        {
            // Best effort only.
        }
    }
}
