// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XamlLanguageServer;

// Server-lifetime singleton. Owns the MetadataLoadContext, discovers @assemblies
// folders above open .vxaml files, and serves up each requested assembly wrapped
// in XamlAssembly (which owns the namespace → type index).
//
// Convention: each project tree has a folder named "@assemblies" somewhere above
// the .vxaml files; that folder contains the DLLs whose types the vxaml dialect
// references (A2v10.Xaml + friends). The resolver walks up from a vxaml path once
// per file to discover the folder; the discovered anchor (directory CONTAINING
// the @assemblies folder) is remembered so future vxaml files in the same subtree
// hit it via StartsWith without re-walking.
//
// MetadataLoadContext is reflection-only: no type initializers run, no file lock
// on the DLLs (so the user can rebuild their A2v10.Xaml.dll without killing VS),
// no AppDomain pollution.
internal sealed class AssemblyResolver : MetadataAssemblyResolver, IDisposable
{
    private const String FolderName = "@assemblies";

    private readonly List<String> _anchors = [];   // dirs containing @assemblies
    private readonly HashSet<String> _folders = new(StringComparer.OrdinalIgnoreCase); // @assemblies paths
    private readonly Dictionary<String, XamlAssembly> _loaded =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly MetadataLoadContext _context;
    private readonly Object _lock = new();

    public AssemblyResolver()
    {
        _context = new MetadataLoadContext(this);
    }

    // Called by MLC whenever it needs to find an assembly — both explicit
    // LoadFromAssemblyName from GetAssembly and implicit cross-assembly refs.
    // @assemblies wins over the runtime directory so a user DLL named like a
    // system one would shadow it (rare but possible). The runtime-directory
    // fallback is mandatory: MLC probes for "mscorlib" during construction and
    // throws if no path comes back.
    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName name)
    {
        var rd = RuntimeEnvironment.GetRuntimeDirectory();

        lock (_lock)
        {
            foreach (var folder in _folders)
            {
                var path = Path.Combine(folder, $"{name.Name}.dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }

            var rtPath = Path.Combine(rd, $"{name.Name}.dll");
            if (File.Exists(rtPath))
                return context.LoadFromAssemblyPath(rtPath);
            return null;
        }
    }

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

    // Loads an assembly by name and wraps it in XamlAssembly. Result is cached
    // by assembly name. No negative caching: a miss leaves the cache untouched
    // so a later EnsureFolderFor can succeed without invalidation.
    public XamlAssembly? GetAssembly(String name)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(name, out var existing))
                return existing;
            try
            {
                var asm = _context.LoadFromAssemblyName(new AssemblyName(name));
                var wrapped = new XamlAssembly(asm);
                _loaded[name] = wrapped;
                return wrapped;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }

    public void Dispose() => _context.Dispose();

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
