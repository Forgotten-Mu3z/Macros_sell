using System.Text.Json;
using EditInput.Core.Logging;

namespace EditInput.Core.Profiles;

/// <summary>
/// Atomic JSON persistence: writes to a temp file then swaps it in, so a crash mid-write never corrupts the
/// previous file. Unreadable files are moved aside (*.corrupt-*) and defaults are used instead.
/// </summary>
public sealed class JsonStore<T> where T : class
{
    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _gate = new();

    public JsonStore(string path, ILogger log)
    {
        _path = path;
        _log = log;
    }

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    public T? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<T>(json, ProfileJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _log.Error("Storage", $"Could not read {System.IO.Path.GetFileName(_path)}; using defaults", ex);
                TryQuarantine();
                return null;
            }
        }
    }

    public bool Save(T value)
    {
        lock (_gate)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value, ProfileJson.Options));
                File.Move(tmp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error("Storage", $"Could not save {System.IO.Path.GetFileName(_path)}", ex);
                return false;
            }
        }
    }

    private void TryQuarantine()
    {
        try { File.Move(_path, $"{_path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true); }
        catch { /* best effort */ }
    }
}
