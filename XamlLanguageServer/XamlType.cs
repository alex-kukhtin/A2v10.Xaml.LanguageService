// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace XamlLanguageServer;

// XAML-level view of a CLR type, independent of any document: the roles it
// plays in the dialect (derived once from its base-type chain — see
// XamlTypeRole) plus its publicly settable properties. The xmlns prefix is
// NOT here — that is a per-document binding, applied when completion entries
// are built. The underlying System.Type is held privately; consumers should
// never reach for it.

[Flags]
internal enum XamlTypeRole
{
    None = 0,
    RootElement = 1,
    MarkupExtension = 2,
}

internal sealed class XamlType
{
    private readonly Type _type;
    private readonly XamlTypeRole _role;
    private XamlProperty[]? _properties;


    private static readonly Dictionary<String, XamlTypeRole> _map_roles = new(StringComparer.Ordinal)
    {
        ["RootContainer"] = XamlTypeRole.RootElement,
        ["Dictionary`2"] = XamlTypeRole.RootElement,
        ["MarkupExtension"] = XamlTypeRole.MarkupExtension
    };

    public XamlType(Type type)
    {
        _type = type;
        Name = type.Name;

        XamlTypeRole role = XamlTypeRole.None;
        for (var b = type.BaseType; b != null; b = b.BaseType)
        {
            if (_map_roles.TryGetValue(b.Name, out var fRole))
                role |= fRole;
        }
        _role = role;
    }

    public String Name { get; }
    public Boolean IsRootElement => _role.HasFlag(XamlTypeRole.RootElement);
    public Boolean IsMarkupExtension => _role.HasFlag(XamlTypeRole.MarkupExtension);

    public XamlProperty[] Properties => _properties ??= BuildProperties();

    private XamlProperty[] BuildProperties()
    {
        var infos = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var list = new List<XamlProperty>(infos.Length);
        foreach (var p in infos)
        {
            if (!p.CanWrite)
                continue;
            list.Add(new XamlProperty(p));
        }
        return list.ToArray();
    }
}
