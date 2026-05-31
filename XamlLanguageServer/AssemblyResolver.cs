// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace XamlLanguageServer;

// Server-lifetime singleton. Discovers @assemblies folders above open .vxaml files
// and serves each requested assembly wrapped in XamlAssembly (which owns the
// namespace -> type index, read straight from metadata).
//
// Convention: each project tree has a folder named "@assemblies" somewhere above
// the .vxaml files; it holds the DLLs whose types the vxaml dialect references
// (A2v10.Xaml + friends). The resolver walks up from a vxaml path once per file to
// find the folder; the discovered anchor (directory CONTAINING @assemblies) is
// remembered so future files in the same subtree hit it via StartsWith without
// re-walking.
//
// No MetadataLoadContext, no reflection: an assembly is read by copying its bytes
// once and handing them to a PEReader / MetadataReader. Because we read the bytes
// rather than memory-mapping the file, the DLL is never locked — the user can
// rebuild A2v10.Xaml.dll without killing VS — and no referenced (system) assembly
// is ever needed, since we only read names from this file's own tables.
internal sealed class AssemblyResolver : IDisposable
{
    private const String FolderName = "@assemblies";

    private readonly List<String> _anchors = [];   // dirs containing @assemblies
    private readonly HashSet<String> _folders = new(StringComparer.OrdinalIgnoreCase); // @assemblies paths
    private readonly Dictionary<String, XamlAssembly> _loaded =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Object _lock = new();

    // Ensures the @assemblies folder above this .vxaml file (if any) is registered.
    // Idempotent and cheap: subsequent calls for files in the same subtree match an
    // existing anchor via StartsWith and return without touching the filesystem.
    public void EnsureFolderFor(String vxamlPath)
    {
        if (String.IsNullOrEmpty(vxamlPath))
            return;
        var full = Path.GetFullPath(vxamlPath);
        lock (_lock)
        {
            foreach (var anchor in _anchors)
                if (full.StartsWith(anchor, StringComparison.OrdinalIgnoreCase))
                    return;

            var found = FindAnchor(full);
            if (found != null)
            {
                _anchors.Add(found);
                _folders.Add(Path.Combine(found, FolderName));
            }
        }
    }

    // Loads an assembly by name and wraps it in XamlAssembly. Cached by name. No
    // negative caching: a miss leaves the cache untouched so a later EnsureFolderFor
    // can succeed without invalidation.
    public XamlAssembly? GetAssembly(String name)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(name, out var existing))
                return existing;

            var path = FindDll(name);
            if (path == null)
                return null;

            var bytes = File.ReadAllBytes(path);
            using var pe = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(bytes));
            var wrapped = new XamlAssembly(pe.GetMetadataReader());
            _loaded[name] = wrapped;
            return wrapped;
        }
    }

    public void Dispose()
    {
    }

    private String? FindDll(String name)
    {
        foreach (var folder in _folders)
        {
            var path = Path.Combine(folder, $"{name}.dll");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static String? FindAnchor(String vxamlPath)
    {
        var dir = Path.GetDirectoryName(vxamlPath);
        while (!String.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, FolderName)))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
