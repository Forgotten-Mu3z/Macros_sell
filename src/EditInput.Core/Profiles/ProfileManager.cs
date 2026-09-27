using EditInput.Core.Logging;

namespace EditInput.Core.Profiles;

public sealed class ProfileFile
{
    public int Version { get; set; } = 1;
    public List<Profile> Profiles { get; set; } = new();
}

/// <summary>
/// Owns the saved profiles (profiles.json). Profiles are always handed out as copies so the UI's working
/// copy can't mutate saved state behind the manager's back. Not thread-safe: UI thread only.
/// </summary>
public sealed class ProfileManager
{
    private readonly JsonStore<ProfileFile> _store;
    private readonly List<Profile> _profiles = new();

    public ProfileManager(string dataDirectory, ILogger log)
    {
        _store = new JsonStore<ProfileFile>(System.IO.Path.Combine(dataDirectory, "profiles.json"), log);
        var file = _store.Load();
        if (file?.Profiles is { Count: > 0 })
        {
            foreach (var p in file.Profiles.Where(p => p is not null))
            {
                p.Normalize();
                if (!Contains(p.Name)) _profiles.Add(p);
            }
        }

        if (_profiles.Count == 0)
        {
            _profiles.AddRange(Profile.CreateDefaults());
            Persist();
        }
    }

    public IReadOnlyList<string> Names => _profiles.Select(p => p.Name).ToList();

    public event Action? ListChanged;

    public bool Contains(string name) => _profiles.Any(p => Same(p.Name, name));

    public Profile? Get(string name) => _profiles.FirstOrDefault(p => Same(p.Name, name))?.Clone();

    public Profile GetOrFirst(string name) => Get(name) ?? _profiles[0].Clone();

    public Profile Create(string name)
    {
        name = RequireUniqueName(name);
        var p = new Profile { Name = name }.Normalize();
        _profiles.Add(p);
        Persist();
        return p.Clone();
    }

    /// <summary>Saves (overwrites) the profile with the same name, or adds it.</summary>
    public void Save(Profile profile)
    {
        var copy = profile.Clone().Normalize();
        var index = _profiles.FindIndex(p => Same(p.Name, copy.Name));
        if (index >= 0) _profiles[index] = copy;
        else _profiles.Add(copy);
        Persist();
    }

    public Profile Duplicate(string sourceName, string newName)
    {
        var source = _profiles.FirstOrDefault(p => Same(p.Name, sourceName))
                     ?? throw new InvalidOperationException($"Profile '{sourceName}' not found.");
        var copy = source.Clone();
        copy.Name = RequireUniqueName(newName);
        _profiles.Add(copy);
        Persist();
        return copy.Clone();
    }

    public void Rename(string oldName, string newName)
    {
        var p = _profiles.FirstOrDefault(x => Same(x.Name, oldName))
                ?? throw new InvalidOperationException($"Profile '{oldName}' not found.");
        newName = (newName ?? "").Trim();
        if (!Same(oldName, newName)) newName = RequireUniqueName(newName);
        else if (newName.Length == 0) throw new ArgumentException("Profile name cannot be empty.");
        p.Name = newName;
        Persist();
    }

    public void Delete(string name)
    {
        if (_profiles.Count <= 1) throw new InvalidOperationException("You can't delete the last profile.");
        var removed = _profiles.RemoveAll(p => Same(p.Name, name));
        if (removed == 0) throw new InvalidOperationException($"Profile '{name}' not found.");
        Persist();
    }

    public string SuggestName(string baseName)
    {
        if (!Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
            if (!Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    private string RequireUniqueName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Profile name cannot be empty.");
        if (name.Length > 48) throw new ArgumentException("Profile name must be 48 characters or fewer.");
        if (Contains(name)) throw new ArgumentException($"A profile named '{name}' already exists.");
        return name;
    }

    private void Persist()
    {
        _store.Save(new ProfileFile { Profiles = _profiles });
        ListChanged?.Invoke();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
