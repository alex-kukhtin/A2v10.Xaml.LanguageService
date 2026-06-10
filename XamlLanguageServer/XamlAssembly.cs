// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;

namespace XamlLanguageServer;

// One assembly read through a MetadataReader. The namespace -> typeName -> XamlType
// index is built once, eagerly, in the constructor: every fact a XamlType needs is
// read out of the metadata tables into plain strings, so nothing here outlives the
// reader (the caller may dispose the PEReader once the constructor returns).
//
// No System.Type is ever materialised and no referenced assembly is loaded: type,
// base and interface NAMES live physically in this file's TypeDef/TypeRef tables
// and in TypeSpec signature blobs, all readable locally. The base-class walk
// follows TypeDefinition.BaseType while it stays inside this file and reads the
// terminal TypeReference's name when the chain crosses into a sibling — enough to
// match a dialect role marker by name without opening the other file.
internal sealed class XamlAssembly
{
    private readonly Dictionary<String, Dictionary<String, XamlType>> _byNamespace =
        new(StringComparer.Ordinal);

    private static readonly MetaSignatureProvider _sig = new();

    // Dialect roles are recognised by the simple name of a base type (classes,
    // including the generic Dictionary`2). Matched against the assembled base-name
    // list — see XamlType for why everything is by-name.
    private static readonly Dictionary<String, XamlTypeRole> _map_roles = new(StringComparer.Ordinal)
    {
        ["RootContainer"] = XamlTypeRole.RootElement,
        ["Dictionary`2"] = XamlTypeRole.RootElement,
        ["MarkupExtension"] = XamlTypeRole.MarkupExtension,
        ["UIElement"] = XamlTypeRole.UiElement,
        ["Inline"] = XamlTypeRole.Inline
    };

    public XamlAssembly(MetadataReader reader)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(handle);
            if (!IsCandidate(reader, td, out var ns, out var name))
                continue;

            CollectHierarchy(reader, td, out var baseClassNames, out var baseNames);
            // Enum: immediate base is System.Enum. Excluded like interfaces and
            // abstract types — never a XAML element.
            if (baseClassNames.Count > 0 && baseClassNames[0] == "Enum")
                continue;

            var type = BuildType(reader, td, name, baseClassNames, baseNames);

            if (!_byNamespace.TryGetValue(ns, out var dict))
            {
                dict = new Dictionary<String, XamlType>(StringComparer.Ordinal);
                _byNamespace[ns] = dict;
            }
            dict[name] = type;
        }
    }

    public XamlType? GetType(String clrNamespace, String typeName) =>
        _byNamespace.TryGetValue(clrNamespace, out var byName) &&
        byName.TryGetValue(typeName, out var t)
            ? t : null;

    public IEnumerable<XamlType> GetTypes(String clrNamespace) =>
        _byNamespace.TryGetValue(clrNamespace, out var byName)
            ? byName.Values
            : [];

    // Top-level public, concrete, named types only — the same filter the old
    // reflection index applied (public, non-interface, non-abstract, non-null
    // namespace). Enums are filtered later, once the base is known.
    private static Boolean IsCandidate(MetadataReader reader, TypeDefinition td, out String ns, out String name)
    {
        ns = String.Empty;
        name = String.Empty;
        var attrs = td.Attributes;
        if ((attrs & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            return false;
        if ((attrs & TypeAttributes.Interface) != 0 || (attrs & TypeAttributes.Abstract) != 0)
            return false;
        ns = reader.GetString(td.Namespace);
        if (ns.Length == 0)
            return false;
        name = reader.GetString(td.Name);
        return true;
    }

    private static XamlType BuildType(MetadataReader reader, TypeDefinition td, String name,
        List<String> baseClassNames, HashSet<String> baseNames)
    {
        XamlTypeRole role = XamlTypeRole.None;
        foreach (var bn in baseClassNames)
            if (_map_roles.TryGetValue(bn, out var fRole))
                role |= fRole;

        var contentProperty = ReadCustomAttribute(reader, td, "ContentProperty");
        var contentPropertyType = contentProperty == null
            ? null
            : ResolveContentElementType(reader, td, contentProperty);

        var attachedProperites = ReadCustomAttribute(reader, td, "AttachedProperties");

        return new XamlType(name, role, baseNames, contentProperty, contentPropertyType,
            ReadSettableProperties(reader, td), attachedProperites?.Split(','));
    }

    // One walk up the base-class chain, producing both:
    //   baseClassNames — ordered simple names of the base classes (for role
    //                    matching and enum detection);
    //   baseNames      — the membership set behind IsA: those base-class names plus
    //                    every dialect interface declared on the type OR on any of
    //                    its same-file base classes. Collecting the bases' own
    //                    interfaces is what reconstructs the transitive closure
    //                    (e.g. SheetGroupCell : SheetCellGroup : ISheetCell — the
    //                    interface is inherited, not declared on SheetGroupCell).
    // While the base is a TypeDefinition it is in this file, so we keep climbing
    // and reading its interfaces; a TypeReference or generic TypeSpecification base
    // is read by name and ends the walk (its definition lives in a file we do not
    // open — the name alone is enough to match a role marker).
    private static void CollectHierarchy(MetadataReader reader, TypeDefinition td,
        out List<String> baseClassNames, out HashSet<String> baseNames)
    {
        baseClassNames = [];
        baseNames = new HashSet<String>(StringComparer.Ordinal);

        foreach (var iface in DirectInterfaceNames(reader, td))
            baseNames.Add(iface);

        var handle = td.BaseType;
        while (!handle.IsNil)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    var bd = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var bn = reader.GetString(bd.Name);
                    baseClassNames.Add(bn);
                    baseNames.Add(bn);
                    foreach (var iface in DirectInterfaceNames(reader, bd))
                        baseNames.Add(iface);
                    handle = bd.BaseType;
                    break;
                case HandleKind.TypeReference:
                    var refName = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name);
                    baseClassNames.Add(refName);
                    baseNames.Add(refName);
                    return;
                case HandleKind.TypeSpecification:
                    var specName = reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                        .DecodeSignature(_sig, null).Name;
                    baseClassNames.Add(specName);
                    baseNames.Add(specName);
                    return;
                default:
                    return;
            }
        }
    }

    // Directly-declared interfaces, restricted to the dialect's own (A2v10.*).
    // Generic interfaces (TypeSpecification) are skipped — content element types
    // are always simple names, so a closed generic interface never matters here.
    private static IEnumerable<String> DirectInterfaceNames(MetadataReader reader, TypeDefinition td)
    {
        foreach (var ih in td.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ih).Interface;
            String name, ns;
            switch (iface.Kind)
            {
                case HandleKind.TypeReference:
                    var tr = reader.GetTypeReference((TypeReferenceHandle)iface);
                    name = reader.GetString(tr.Name);
                    ns = reader.GetString(tr.Namespace);
                    break;
                case HandleKind.TypeDefinition:
                    var d = reader.GetTypeDefinition((TypeDefinitionHandle)iface);
                    name = reader.GetString(d.Name);
                    ns = reader.GetString(d.Namespace);
                    break;
                default:
                    continue;
            }
            if (ns.StartsWith("A2v10", StringComparison.Ordinal))
                yield return name;
        }
    }

    private static String? ReadCustomAttribute(MetadataReader reader, TypeDefinition td, String attrName)
    {
        foreach (var ah in td.GetCustomAttributes())
        {
            var ca = reader.GetCustomAttribute(ah);
            if (AttributeTypeName(reader, ca) != $"{attrName}Attribute")
                continue;
            var value = ca.DecodeValue(_sig);
            if (value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is String s)
                return s;
        }
        return null;
    }

    // The element type that a content property constrains children to. A typed
    // collection hides it one base hop down (Rows : TableRowCollection : List<T>):
    // we read the property type, and if it is a same-file class whose base is a
    // generic List-like, take that single generic argument. A bare List<T> property
    // is taken directly. Anything else is its own name (Object for unconstrained).
    private static String? ResolveContentElementType(MetadataReader reader, TypeDefinition td, String propertyName)
    {
        foreach (var ph in td.GetProperties())
        {
            var pd = reader.GetPropertyDefinition(ph);
            if (reader.GetString(pd.Name) != propertyName)
                continue;

            var propType = pd.DecodeSignature(_sig, null).ReturnType;
            return TryResolveCollectionElementType(reader, propType, out var arg) ? arg : propType.Name;
        }
        return null;
    }

    // The element type of a collection property: the single generic argument of a
    // List<T> property, or of a same-file collection class whose base is List<T>
    // (TableRowCollection : List<TableRow> -> TableRow). Returns false for anything
    // that is not such a collection — a scalar property has no element type.
    private static Boolean TryResolveCollectionElementType(MetadataReader reader, MetaTypeRef propType, out String elementType)
    {
        if (TryGetSingleGenericArg(propType, out elementType))
            return true;

        // The List<T> may sit several levels up the base-class chain, not on the
        // property type directly (Cells : TableCellCollection : UIElementCollection
        // : List<UIElementBase>). Walk up while each base stays a TypeDefinition in
        // this file; the generic collection appears as a TypeSpecification base —
        // decode it and take the single type argument. Stops at the first external
        // base (a TypeReference / nil), which we cannot follow.
        if (!propType.Handle.IsNil && propType.Handle.Kind == HandleKind.TypeDefinition)
        {
            var current = reader.GetTypeDefinition((TypeDefinitionHandle)propType.Handle);
            while (true)
            {
                var baseHandle = current.BaseType;
                if (baseHandle.Kind == HandleKind.TypeSpecification)
                {
                    var baseType = reader.GetTypeSpecification((TypeSpecificationHandle)baseHandle)
                        .DecodeSignature(_sig, null);
                    return TryGetSingleGenericArg(baseType, out elementType);
                }
                if (baseHandle.Kind != HandleKind.TypeDefinition)
                    break; // external base (sibling assembly / System.Object) or nil
                current = reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle);
            }
        }
        elementType = String.Empty;
        return false;
    }

    // A Nullable<T> property is a generic instance of System.Nullable (named
    // "Nullable`1" in metadata); unwrap to the single type argument so a
    // Boolean? reads as Boolean and a same-file enum keeps the TypeDefinition
    // handle its members are read from. Non-Nullable types pass through.
    private static MetaTypeRef UnwrapNullable(MetaTypeRef type) =>
        type.IsGenericInstance && type.Namespace == "System"
            && type.Name.StartsWith("Nullable", StringComparison.Ordinal)
            && type.GenericArgs.Count == 1
            ? type.GenericArgs[0]
            : type;

    private static Boolean TryGetSingleGenericArg(MetaTypeRef type, out String name)
    {
        if (type.IsGenericInstance && type.GenericArgs.Count == 1)
        {
            name = type.GenericArgs[0].Name;
            return true;
        }
        name = String.Empty;
        return false;
    }

    // Settable properties of the type AND of every base class reachable as a
    // TypeDefinition in this file. Attribute completion needs the inherited
    // surface: Button : ... : UIElement must offer Width/Height etc., which are
    // declared up the chain. The walk follows BaseType while it stays in this
    // file; an external base (the terminal System.Object TypeRef, or a sibling
    // assembly) ends it — those members live in a file we do not open. A name
    // first seen wins, so a derived declaration shadows the inherited one.
    private static XamlProperty[] ReadSettableProperties(MetadataReader reader, TypeDefinition td)
    {
        var list = new List<XamlProperty>();
        var seen = new HashSet<String>(StringComparer.Ordinal);

        var current = td;
        while (true)
        {
            foreach (var ph in current.GetProperties())
            {
                var pd = reader.GetPropertyDefinition(ph);
                var accessors = pd.GetAccessors();
                if (accessors.Setter.IsNil)
                    continue;
                var setter = reader.GetMethodDefinition(accessors.Setter);
                if ((setter.Attributes & MethodAttributes.Static) != 0)
                    continue;
                // Settable FROM XAML means the SETTER is public — a public
                // getter with a private setter reads fine in C# but cannot be
                // assigned by an attribute. The getter's visibility is
                // irrelevant here.
                if ((setter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                // Unwrap Nullable<T> (Boolean?, SomeEnum?) to its underlying T:
                // completion cares about the bool-ness / enum members of T, not
                // the wrapper. Only value types are ever Nullable, so the
                // collection / delegate checks below are unaffected.
                var propType = UnwrapNullable(pd.DecodeSignature(_sig, null).ReturnType);
                // Delegate-typed members (Action, Action<…> — the latter named
                // "Action`1" etc. in metadata) are code callbacks, never XAML
                // attributes; keep them out of completion (and out of the dedup
                // set, so a settable same-named member higher up still counts).
                if (propType.Name == "Action" || propType.Name.StartsWith("Action`", StringComparison.Ordinal))
                    continue;

                var name = reader.GetString(pd.Name);
                if (!seen.Add(name))
                    continue;

                var contentElementType = TryResolveCollectionElementType(reader, propType, out var elem) ? elem : null;
                list.Add(new XamlProperty(name, propType.Name, contentElementType, ReadEnumValues(reader, propType)));
            }

            if (current.BaseType.Kind != HandleKind.TypeDefinition)
                break;
            current = reader.GetTypeDefinition((TypeDefinitionHandle)current.BaseType);
        }
        return [.. list];
    }

    // Enum member names, but only when the enum is defined in THIS file (its base
    // is a readable System.Enum reference). An enum from another assembly would
    // need that file opened — out of scope; EnumValues stays null there.
    private static String[]? ReadEnumValues(MetadataReader reader, MetaTypeRef propType)
    {
        if (propType.Handle.IsNil || propType.Handle.Kind != HandleKind.TypeDefinition)
            return null;
        var def = reader.GetTypeDefinition((TypeDefinitionHandle)propType.Handle);
        if (def.BaseType.Kind != HandleKind.TypeReference)
            return null;
        if (reader.GetString(reader.GetTypeReference((TypeReferenceHandle)def.BaseType).Name) != "Enum")
            return null;

        var names = new List<String>();
        foreach (var fh in def.GetFields())
        {
            var fd = reader.GetFieldDefinition(fh);
            if ((fd.Attributes & FieldAttributes.Static) != 0 && (fd.Attributes & FieldAttributes.Literal) != 0)
                names.Add(reader.GetString(fd.Name));
        }
        return names.Count > 0 ? [.. names] : null;
    }

    private static String AttributeTypeName(MetadataReader reader, CustomAttribute ca)
    {
        switch (ca.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var mr = reader.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                return mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)mr.Parent).Name),
                    HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)mr.Parent).Name),
                    _ => String.Empty
                };
            case HandleKind.MethodDefinition:
                var md = reader.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                return reader.GetString(reader.GetTypeDefinition(md.GetDeclaringType()).Name);
            default:
                return String.Empty;
        }
    }
}
