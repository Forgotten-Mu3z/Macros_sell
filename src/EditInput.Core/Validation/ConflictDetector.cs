using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Profiles;

namespace EditInput.Core.Validation;

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record BindIssue(IssueSeverity Severity, string Message)
{
    public string Icon => Severity switch
    {
        IssueSeverity.Error => "⛔",
        IssueSeverity.Warning => "⚠",
        _ => "ℹ",
    };
}

/// <summary>Finds conflicting or ineffective binds. Warnings never block saving; errors describe unsafe setups.</summary>
public static class ConflictDetector
{
    public static IReadOnlyList<BindIssue> Analyze(Profile p, AppSettings s, bool controllerOutputAvailable)
    {
        var issues = new List<BindIssue>();
        var cfg = EngineConfig.From(p, s);

        if (s.EmergencyStop.IsNone)
            issues.Add(new(IssueSeverity.Error, "Emergency Stop has no bind."));

        if (p.Mode == EngineMode.EditAutomation) AnalyzeAutomation(p, s, issues);
        else AnalyzeRemap(p, s, issues);

        // Emergency stop must never be something the app itself generates.
        foreach (var output in cfg.OutputInputs().Distinct())
        {
            if (output == s.EmergencyStop)
                issues.Add(new(IssueSeverity.Error, $"Emergency Stop ({output.DisplayName}) is also an input this app generates. Choose a different Emergency Stop key."));
            if (output == s.ToggleBind && !output.IsNone)
                issues.Add(new(IssueSeverity.Warning, $"Enable/Disable ({output.DisplayName}) is also an input this app generates."));
        }

        foreach (var output in cfg.OutputInputs().Where(o => o.Kind == DeviceKind.Controller).Distinct())
        {
            if (!controllerOutputAvailable)
            {
                issues.Add(new(IssueSeverity.Warning,
                    $"{output.DisplayName} is a controller output, which needs the ViGEmBus virtual controller driver. It is not installed, so it will not be sent."));
                break;
            }
        }

        foreach (var (name, id) in Named(p, s))
        {
            if (id.IsNone) continue;
            if (!cfg.DeviceAllowed(id) && name is "Edit" or "Select" or "Reset" or "Confirm")
                issues.Add(new(IssueSeverity.Warning,
                    $"{name} uses a {(id.Kind == DeviceKind.Controller ? "controller" : "keyboard/mouse")} input but Device Mode is {DeviceModeName(p.DeviceMode)}; it will be ignored."));
        }

        return issues;
    }

    private static void AnalyzeAutomation(Profile p, AppSettings s, List<BindIssue> issues)
    {
        if (p.EditBind.IsNone) issues.Add(new(IssueSeverity.Warning, "Edit has no bind, so automation never starts."));
        if (p.SelectBind.IsNone) issues.Add(new(IssueSeverity.Warning, "Select has no bind, so nothing will be held."));
        if (p.ResetBeforeSelect && p.ResetBind.IsNone)
            issues.Add(new(IssueSeverity.Warning, "Reset Before Select is ON but Reset has no bind."));
        if (p.AutoConfirm != AutoConfirmMode.Off && p.ConfirmBind.IsNone)
            issues.Add(new(IssueSeverity.Warning, "Auto Confirm is ON but Confirm has no bind."));

        var named = Named(p, s).Where(n => !n.Id.IsNone).ToList();
        for (var i = 0; i < named.Count; i++)
        for (var j = i + 1; j < named.Count; j++)
        {
            if (named[i].Id != named[j].Id) continue;
            var sev = named[i].Name == "Emergency Stop" || named[j].Name == "Emergency Stop"
                ? IssueSeverity.Error
                : IssueSeverity.Warning;
            issues.Add(new(sev, $"{named[i].Name} and {named[j].Name} are using the same input ({named[i].Id.DisplayName})."));
        }

        if (p.SelectMode == SelectMode.Toggle)
            issues.Add(new(IssueSeverity.Info, "Toggle mode keeps Select held after Edit is released. Press Edit again (or F12) to release it."));
    }

    private static void AnalyzeRemap(Profile p, AppSettings s, List<BindIssue> issues)
    {
        var valid = p.Remaps.Where(r => !r.Source.IsNone && !r.Target.IsNone).ToList();
        if (valid.Count == 0) issues.Add(new(IssueSeverity.Warning, "Simple Remap mode is on but no complete mappings exist."));
        if (p.Remaps.Any(r => r.Source.IsNone || r.Target.IsNone))
            issues.Add(new(IssueSeverity.Info, "Mappings with an empty side are ignored."));

        foreach (var g in valid.GroupBy(r => r.Source).Where(g => g.Count() > 1))
            issues.Add(new(IssueSeverity.Warning, $"{g.Key.DisplayName} is mapped more than once; only the first mapping is used (strict one-to-one)."));
        foreach (var g in valid.GroupBy(r => r.Target).Where(g => g.Count() > 1))
            issues.Add(new(IssueSeverity.Warning, $"Several sources map to {g.Key.DisplayName}; releasing one releases it for all."));

        var sources = valid.Select(r => r.Source).ToHashSet();
        foreach (var r in valid)
        {
            if (r.Source == r.Target) issues.Add(new(IssueSeverity.Info, $"{r.Source.DisplayName} maps to itself and is ignored."));
            else if (sources.Contains(r.Target))
                issues.Add(new(IssueSeverity.Warning, $"{r.Target.DisplayName} is both a source and a target. Generated input never re-triggers mappings, so no chain will happen."));
            if (r.Source == s.EmergencyStop || r.Source == s.ToggleBind)
                issues.Add(new(IssueSeverity.Warning, $"{r.Source.DisplayName} is used by a global hotkey and as a remap source."));
            if (r.Source.Kind == DeviceKind.Controller)
                issues.Add(new(IssueSeverity.Info, $"{r.Source.DisplayName}: controller buttons can't be hidden from games, so the game will see both the original and the target."));
        }
    }

    private static IEnumerable<(string Name, InputId Id)> Named(Profile p, AppSettings s)
    {
        if (p.Mode == EngineMode.EditAutomation)
        {
            yield return ("Edit", p.EditBind);
            yield return ("Select", p.SelectBind);
            yield return ("Reset", p.ResetBind);
            yield return ("Confirm", p.ConfirmBind);
        }
        yield return ("Enable/Disable", s.ToggleBind);
        yield return ("Emergency Stop", s.EmergencyStop);
    }

    public static string DeviceModeName(DeviceMode m) => m switch
    {
        DeviceMode.KeyboardMouse => "Keyboard & Mouse",
        DeviceMode.Controller => "Controller",
        _ => "Hybrid",
    };
}
