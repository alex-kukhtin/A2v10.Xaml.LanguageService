// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace XamlLanguageServer;

// XAML-level view of a CLR type: the xmlns prefix it appears under in the
// current document plus its publicly settable properties. The underlying
// System.Type is held privately; consumers should never reach for it.
internal sealed class XamlType(String _prefix, Type _type)
{
    private XamlProperty[]? _properties;

    public String Prefix { get; } = _prefix;
    public String Name => _type.Name;

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
