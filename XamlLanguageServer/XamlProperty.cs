// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;

namespace XamlLanguageServer;

// Settable property surfaced for attribute completion. Plain data extracted from
// metadata (no live PropertyInfo): the declared property type's simple name and,
// when that type is an enum defined in the same assembly, its member names.
internal sealed class XamlProperty(String name, String type, String? contentElementType, String[]? enumValues)
{
    public String Name { get; } = name;
    public String Type { get; } = type;
    // The element type when this property is a collection (TableRowCollection :
    // List<TableRow> -> TableRow) — what may sit inside the property element
    // <Owner.Name>. Null for scalar properties: only collections drive that
    // completion. Decoded by the same logic as XamlType.ContentPropertyType.
    public String? ContentElementType { get; } = contentElementType;
    public String[]? EnumValues { get; } = enumValues;
}
