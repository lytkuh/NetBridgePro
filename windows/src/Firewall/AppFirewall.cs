using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

using NetBridge.Windows.Core;

using System.IO;

namespace NetBridge.Windows.Firewall;

/// <summary>
/// Per-app internet blocking via Windows Firewall (INetFwPolicy2 COM).
/// Rules are prefixed "NetBridge" and persisted to ProgramData so they are
/// reapplied after reboot.
/// </summary>
public static class AppFirewall
{
    private const string RulePrefix = "NetBridge block";

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public class AppEntry
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Blocked { get; set; }
    }

    public static ObservableCollection<AppEntry> ListedApps { get; } = new();

    // ---------------- rules ---------------- //

    private static dynamic? Policy()
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            return t == null ? null : Activator.CreateInstance(t);
        }
        catch (Exception ex)
        {
            Logger.Warn("Firewall", "policy unavailable: " + ex.Message);
            return null;
        }
    }

    public static bool IsBlocked(string appPath)
    {
        var policy = Policy();
        if (policy == null) return false;
        try
        {
            foreach (var rule in policy.Rules)
            {
                if (rule is not null && ((string)rule.Name).StartsWith(RulePrefix) &&
                    string.Equals((string)rule.ApplicationName, appPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    public static bool SetBlocked(string appPath, bool blocked)
    {
        if (!IsElevated)
        {
            Logger.Warn("Firewall", "elevation required to modify firewall rules");
            return false;
        }
        var policy = Policy();
        if (policy == null) return false;
        try
        {
            // remove any existing rule for this app
            var toRemove = new List<dynamic>();
            foreach (var rule in policy.Rules)
            {
                if (rule is not null && ((string)rule.Name).StartsWith(RulePrefix) &&
                    string.Equals((string)rule.ApplicationName, appPath, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(rule);
            }
            foreach (var r in toRemove) policy.Rules.Remove(r.Name);

            if (blocked)
            {
                var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
                if (ruleType == null) return false;
                dynamic rule = Activator.CreateInstance(ruleType)!;
                rule.Name = $"{RulePrefix} {Path.GetFileName(appPath)}";
                rule.Description = "Blocked by NetBridge Pro";
                rule.ApplicationName = appPath;
                rule.Action = 0;          // NET_FW_ACTION_BLOCK
                rule.Direction = 2;       // NET_FW_RULE_DIR_OUT
                rule.Enabled = true;
                rule.Protocol = 256;      // any
                policy.Rules.Add(rule);
            }

            PersistRules();
            Logger.Info("Firewall", $"{(blocked ? "blocked" : "allowed")}: {appPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Firewall", "SetBlocked failed: " + ex.Message);
            return false;
        }
    }

    public static void ReapplySavedRules()
    {
        try
        {
            var saved = LoadRules();
            if (saved.Count == 0) return;
            if (!IsElevated)
            {
                Logger.Warn("Firewall", $"{saved.Count} saved rule(s) need elevation to reapply");
                return;
            }
            foreach (var path in saved)
            {
                if (!IsBlocked(path)) SetBlocked(path, true);
            }
            Logger.Info("Firewall", $"reapplied {saved.Count} saved rule(s)");
        }
        catch (Exception ex)
        {
            Logger.Warn("Firewall", "reapply failed: " + ex.Message);
        }
    }

    // ---------------- persistence ---------------- //

    private static List<string> LoadRules()
    {
        try
        {
            if (System.IO.File.Exists(Constants.FirewallRulesFile))
                return JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(Constants.FirewallRulesFile)) ?? new();
        }
        catch { }
        return new List<string>();
    }

    private static void PersistRules()
    {
        try
        {
            var list = new List<string>();
            var policy = Policy();
            if (policy == null) return;
            foreach (var rule in policy.Rules)
            {
                if (rule is not null && ((string)rule.Name).StartsWith(RulePrefix))
                    list.Add((string)rule.ApplicationName);
            }
            System.IO.File.WriteAllText(Constants.FirewallRulesFile,
                JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("Firewall", "persist failed: " + ex.Message);
        }
    }

    // ---------------- app discovery ---------------- //

    public static void RefreshRunningApps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // start with currently saved rules so they always show up
        foreach (var p in LoadRules()) seen.Add(p);

        var list = new List<AppEntry>();
        foreach (var saved in seen)
        {
            list.Add(new AppEntry
            {
                Path = saved,
                Name = System.IO.Path.GetFileNameWithoutExtension(saved),
                Blocked = IsBlocked(saved)
            });
        }

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                string? path = null;
                try { path = proc.MainModule?.FileName; } catch { }
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                if (path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;

                list.Add(new AppEntry
                {
                    Path = path,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Blocked = IsBlocked(path)
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("Firewall", "enumerate: " + ex.Message);
        }

        ListedApps.Clear();
        foreach (var e in list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            ListedApps.Add(e);
    }
}
