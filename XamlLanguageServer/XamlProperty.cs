// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Reflection;

namespace XamlLanguageServer;

// Settable CLR property on a XAML type, surfaced for attribute completion.
// Built from PropertyInfo under MetadataLoadContext, so enum values come from
// GetFields(Public|Static) — NOT Enum.GetValues / typeof() — see
// project_mlc_enum_inspection memory.
internal sealed class XamlProperty
{
    public String Name { get; }
    public String Type { get; }
    public String[]? EnumValues { get; }

    public XamlProperty(PropertyInfo property)
    {
        Name = property.Name;
        var t = property.PropertyType;
        Type = t.Name;
        if (t.IsEnum)
            EnumValues = ReadEnumNames(t);
    }

    private static String[] ReadEnumNames(Type enumType)
    {
        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
        var names = new String[fields.Length];
        for (var i = 0; i < fields.Length; i++)
            names[i] = fields[i].Name;
        return names;
    }
}
