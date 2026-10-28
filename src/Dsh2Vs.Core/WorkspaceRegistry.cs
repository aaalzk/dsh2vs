using System.Text.Json;

namespace Dsh2Vs.Core;

/// <summary>A named root directory the bridge is allowed to work in.</summary>
public sealed class WorkspaceEntry
{
    public string? Name { get; set; }
    public string? Path { get; set; }
}

public sealed record WorkspaceInfo(string Name, string Path);

/// <summary>
/// Resolves which directory a relative path belongs to, and enforces the safety boundary
/// ("only touch files inside a configured workspace").
///
/// Sources, in order: the workspaces file (editable without rebuilding), the "Dsh2Vs:Workspaces"
/// configuration array, and finally the legacy single "Dsh2Vs:WorkspaceRoot".
/// </summary>
public sealed class WorkspaceRegistry
{
    private readonly List<WorkspaceInfo> _items = new();
    private string _currentName = "default";

    public WorkspaceRegistry(Dsh2VsOptions options)
    {
        var file = options.WorkspacesFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh2vs",
                "workspaces.json");
        }

        WorkspacesFile = file;

        if (File.Exists(file))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<WorkspaceEntry>>(
                    File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                foreach (var entry in entries)
                {
                    Add(entry.Name, entry.Path);
                }
            }
            catch
            {
                // A broken workspaces file must never stop the bridge from starting.
            }
        }

        foreach (var entry in options.Workspaces)
        {
            Add(entry.Name, entry.Path);
        }

        Add("default", options.WorkspaceRoot);

        if (_items.Count == 0)
        {
            Add("default", Environment.CurrentDirectory);
        }

        // An explicitly configured WorkspaceRoot wins over the file's first entry: the operator
        // said "this is the project I care about", and the service should start there.
        var preferred = string.IsNullOrWhiteSpace(options.WorkspaceRoot)
            ? null
            : _items.FirstOrDefault(i => i.Path.Equals(SafeFullPath(options.WorkspaceRoot), StringComparison.OrdinalIgnoreCase));

        _currentName = preferred?.Name ?? _items[0].Name;
        options.WorkspaceRoot ??= _items[0].Path;
    }

    public string WorkspacesFile { get; }

    public IReadOnlyList<WorkspaceInfo> All => _items;

    public string CurrentName => _currentName;

    public WorkspaceInfo? Current =>
        _items.FirstOrDefault(i => i.Name.Equals(_currentName, StringComparison.OrdinalIgnoreCase)) ?? _items.FirstOrDefault();

    public bool Use(string name)
    {
        var match = _items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        _currentName = match.Name;
        return true;
    }

    public WorkspaceInfo? Find(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return null;
        }

        var byName = _items.FirstOrDefault(i => i.Name.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        var candidate = SafeFullPath(nameOrPath);
        return _items.FirstOrDefault(i => i.Path.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the path sits inside any configured workspace.</summary>
    public bool Contains(string path)
    {
        var full = SafeFullPath(path);
        foreach (var item in _items)
        {
            var root = item.Path.EndsWith(Path.DirectorySeparatorChar) ? item.Path : item.Path + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                full.Equals(item.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Absolute paths are used as-is; relative ones resolve against the requested workspace,
    /// then the current one.
    /// </summary>
    public string Resolve(string? workspaceNameOrPath, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var workspace = Find(workspaceNameOrPath) ?? Current
                        ?? throw new InvalidOperationException("No workspace is configured.");

        return Path.GetFullPath(Path.Combine(workspace.Path, path));
    }

    private void Add(string? name, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = SafeFullPath(path);
        if (_items.Any(i => i.Path.Equals(full, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var resolved = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : name.Trim();
        if (string.IsNullOrWhiteSpace(resolved))
        {
            resolved = "workspace";
        }

        var unique = resolved;
        var suffix = 2;
        while (_items.Any(i => i.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)))
        {
            unique = $"{resolved}-{suffix++}";
        }

        _items.Add(new WorkspaceInfo(unique, full));
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }
}
