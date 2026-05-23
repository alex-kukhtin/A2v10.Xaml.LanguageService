// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XamlLanguageServer;

internal class AssemblyResolver(Func<CancellationToken, Task<String?>> _findCsproj) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Boolean _dirty = true;
    private FileSystemWatcher? _watcher;
    private String? _watchedAssetsPath;

    private IReadOnlyList<String>? _cache;
    public void Dispose()
    {
        _watcher?.Dispose();
        _lock.Dispose();
    }
    public async Task<IReadOnlyList<String>> GetAssemblyPathsAsync(
        String assemblyName, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_dirty && _cache is not null)
                return _cache;

            var csprojPath = await _findCsproj(ct);
            if (csprojPath is null)
                return [];

            var assetsPath = Path.Combine(
                Path.GetDirectoryName(csprojPath)!, "obj", "project.assets.json");

            if (!File.Exists(assetsPath))
                return [];

            _cache = ParseAssets(assetsPath);
            _dirty = false;
            WatchAssets(assetsPath);

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
    private void WatchAssets(String assetsPath)
    {
        if (_watchedAssetsPath == assetsPath)
            return;

        _watcher?.Dispose();
        _watchedAssetsPath = assetsPath;
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(assetsPath)!, "project.assets.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += (_, _) => _dirty = true;
        _watcher.Created += (_, _) => _dirty = true;
        _watcher.Renamed += (_, _) => _dirty = true;

        _watcher.EnableRaisingEvents = true;
    }

    private static IReadOnlyList<String> ParseAssets(String assetsPath)
    {
        using var stream = File.OpenRead(assetsPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var packageFolders = root.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();

        var libraries = root.GetProperty("libraries");

        // Take the first TFM from targets.
        var targetPackages = root.GetProperty("targets")
            .EnumerateObject()
            .First()
            .Value;

        var dlls = new List<String>();

        foreach (var pkg in targetPackages.EnumerateObject())
        {
            if (!pkg.Value.TryGetProperty("compile", out var compile))
                continue;

            if (!libraries.TryGetProperty(pkg.Name, out var lib))
                continue;

            var libPath = lib.GetProperty("path").GetString()!;

            foreach (var entry in compile.EnumerateObject())
            {
                var rel = entry.Name;
                if (rel == "_._") continue;

                foreach (var folder in packageFolders)
                {
                    var full = Path.Combine(folder, libPath, rel)
                        .Replace('/', Path.DirectorySeparatorChar);
                    if (File.Exists(full))
                    {
                        dlls.Add(full);
                        break;
                    }
                }
            }
        }

        return dlls;
    }
}
