// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XamlLanguageServer;

// One loaded assembly from @assemblies. Owns its Assembly handle privately
// and exposes only type lookups by CLR namespace. The namespace → typeName
// index is built once on construction (eager — assembly is loaded specifically
// to be queried, so we pay the scan up front and serve every subsequent call
// from a dictionary).
internal sealed class XamlAssembly
{
    private readonly Assembly _assembly;
    private readonly Dictionary<String, Dictionary<String, XamlType>> _byNamespace =
        new(StringComparer.Ordinal);

    public XamlAssembly(Assembly assembly)
    {
        _assembly = assembly;

        foreach (var t in _assembly.GetTypes())
        {
            if (!t.IsPublic || t.Namespace == null || t.IsInterface || t.IsEnum)
                continue;

            if (!_byNamespace.TryGetValue(t.Namespace, out var dict))
            {
                dict = new Dictionary<String, XamlType>(StringComparer.Ordinal);
                _byNamespace[t.Namespace] = dict;
            }
            dict[t.Name] = new XamlType(t);
        }
    }

    public XamlType? GetType(String clrNamespace, String typeName) =>
        _byNamespace.TryGetValue(clrNamespace, out var byName) &&
        byName.TryGetValue(typeName, out var t)
            ? t : null;

    public IEnumerable<XamlType> GetTypes(String clrNamespace) =>
        _byNamespace.TryGetValue(clrNamespace, out var byName)
            ? byName.Values
            : Enumerable.Empty<XamlType>();
}
