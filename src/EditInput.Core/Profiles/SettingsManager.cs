using EditInput.Core.Logging;

namespace EditInput.Core.Profiles;

public sealed class SettingsManager
{
    private readonly JsonStore<AppSettings> _store;
    private AppSettings _current;

    public SettingsManager(string dataDirectory, ILogger log)
    {
        _store = new JsonStore<AppSettings>(System.IO.Path.Combine(dataDirectory, "settings.json"), log);
        IsFirstRun = !_store.Exists;
        _current = (_store.Load() ?? new AppSettings()).Normalize();
        if (!_current.FirstRunCompleted) IsFirstRun = true;
    }

    public bool IsFirstRun { get; private set; }

    /// <summary>Returns a copy; call <see cref="Update"/> to change settings.</summary>
    public AppSettings Current => _current.Clone();

    public event Action<AppSettings>? Changed;

    public void Update(Action<AppSettings> change)
    {
        var copy = _current.Clone();
        change(copy);
        _current = copy.Normalize();
        _store.Save(_current);
        if (_current.FirstRunCompleted) IsFirstRun = false;
        Changed?.Invoke(_current.Clone());
    }
}
