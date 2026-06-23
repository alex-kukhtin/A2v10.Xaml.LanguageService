// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;

namespace XamlLanguageServer;

// XAML-level view of a type, independent of any document: the dialect roles it
// plays, its base/interface names (by simple name), the content-property element
// type, and its settable properties. Pure data — nothing here holds a live
// System.Type or metadata handle; everything was read by name from the assembly's
// metadata tables when the XamlAssembly index was built. The xmlns prefix is NOT
// here — that is a per-document binding (see FullXamlType).

[Flags]
internal enum XamlTypeRole
{
    None = 0,
    RootElement = 1,
    MarkupExtension = 2,
    UiElement = 4,
    Inline = 8,
}

internal sealed class XamlType(
    String name,
    XamlTypeRole role,
    HashSet<String> baseNames,
    String? contentProperty,
    String? contentPropertyType,
    XamlProperty[] properties,
    XamlProperty[] attachedProperties,
    Boolean attachedTransparent)
{
    private readonly XamlTypeRole _role = role;
    // Simple names of every base class (within and across the file boundary, as
    // far as the metadata could be read) plus directly-declared dialect
    // interfaces. The membership set behind IsA.
    private readonly HashSet<String> _baseNames = baseNames;

    public String Name { get; } = name;
    public String? ContentProperty { get; } = contentProperty;
    public String? ContentPropertyType { get; } = contentPropertyType;
    public XamlProperty[] Properties { get; } = properties;

    // Attached properties this type declares for ITS children (Grid -> Row, Col),
    // used dotted on a child: <Block Grid.Row="1"/>. Names come from the
    // runtime-owned [AttachedProperties]; value types from the advisory
    // [AttachedPropertyType] overlay (see XamlAssembly.BuildAttached). Carried as
    // full XamlProperty so the dotted value slot reuses ValueEntries unchanged — a
    // name without an overlay type is "Object" (name completion only). Never null:
    // empty when the attribute is absent, so the ancestor walk iterates freely.
    public XamlProperty[] AttachedProperties { get; } = attachedProperties;

    // Whether an OUTER container's attached properties pass THROUGH this type to
    // its descendants (Grid > <transparent wrapper> > Block lets Block see
    // Grid.Row). Default false — attached reach direct children only, the WPF
    // rule; the [AttachedTransparent] marker opens deeper propagation on the few
    // containers that forward it. Default true would make the marker a permanent
    // no-op, so false is the only meaningful polarity. Read by name, so it lights
    // up automatically once the marker appears on real A2v10 types.
    public Boolean IsAttachedTransparent { get; } = attachedTransparent;

    public Boolean IsRootElement => _role.HasFlag(XamlTypeRole.RootElement);
    public Boolean IsMarkupExtension => _role.HasFlag(XamlTypeRole.MarkupExtension);
    public Boolean IsUiElement => _role.HasFlag(XamlTypeRole.UiElement);
    public Boolean IsInline => _role.HasFlag(XamlTypeRole.Inline);

    // The content property carries a concrete element type that constrains valid
    // children (a typed collection like TableRowCollection : List<TableRow>, or a
    // single typed value). When true, IsVisibleIn is the sole authority and the
    // coarse IsUiElement role filter must step aside. Object / null means the
    // model is unconstrained — fall back to convention.
    public Boolean HasTypedContent => ContentPropertyType != null && ContentPropertyType != "Object";

    // True when this type may sit as a child element under a content model whose
    // element type is `name`: it is `name`, or carries it anywhere in its
    // base-class chain or implemented interfaces (by simple name — the same
    // by-name convention the role map uses), AND is itself usable as a child.
    // Root elements and markup extensions can never sit inside a container, so
    // that exclusion is folded in here rather than repeated at every call site.
    internal Boolean IsAChild(String name) =>
        !IsRootElement && !IsMarkupExtension && (Name == name || _baseNames.Contains(name));

    internal Boolean IsVisibleIn(XamlType type)
    {
        // Typed content collection (Rows : TableRowCollection : List<TableRow>):
        // only children that are, or derive from, the element type are valid.
        if (HasTypedContent)
            return type.IsAChild(ContentPropertyType!);

        // Unconstrained (Object / mixed) content — fall back to dialect
        // convention until a richer content model is wired.
        if (ContentProperty == "Inlines")
            return type.IsInline;
        return true;
    }
}
