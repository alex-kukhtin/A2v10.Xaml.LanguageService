// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

using XamlTolerantParser;

namespace XamlLanguageServer;

// Server-lifetime singleton. Owns every semantic decision the language server
// makes about XAML completion: classifies the offset in a document, resolves
// types through AssemblyResolver, returns a flat list of completion entries.
// Handlers do not reason about context — they call Complete and translate.
internal sealed class XamlContextProvider(AssemblyResolver _resolver)
{
    // Building block: every public type reachable through clr-namespace prefixes
    // in this document. Consumed by Complete and by tests; handlers should call
    // Complete instead.
    public IEnumerable<XamlType> RootTypes(XamlDocument doc)
    {
        foreach (var (prefix, (asmName, ns)) in doc.ClrPrefixes)
        {
            var asm = _resolver.GetAssembly(asmName);
            if (asm == null)
                continue;
            foreach (var t in asm.GetTypes(ns))
            {
                if (t.IsNotPublic || t.IsInterface || t.IsNested)
                    continue;
                yield return new XamlType(prefix, t);
            }
        }
    }

    // The single position-aware entry point. Classifies the cursor (value slot
    // → enum values; attribute-name slot inside an open tag → properties of
    // the element; tag-name slot → root tags) and returns the corresponding
    // entries. Empty list = nothing to suggest. Position is LSP-shaped
    // (0-based line, 0-based UTF-16 column).
    public IReadOnlyList<XamlCompletionEntry> Complete(XamlDocument doc, Int32 line, Int32 column)
    {
        var node = doc.FindNodeAt(line, column);
        // Forward / backward source scans below still need an offset; compute once.
        var offset = doc.OffsetAt(line, column);

        // 1. Attribute value — enum members only (no metadata for arbitrary
        //    string / numeric / type-converter properties yet).
        if (node is XamlValue value &&
            value.Parent is XamlAttribute valueAttr &&
            valueAttr.Owner is { } valueElem)
        {
            return EnumValueEntries(doc, valueElem, valueAttr);
        }

        // 2. Attribute-name slot inside an open tag.
        var attrOwner = node switch
        {
            XamlAttribute a => a.Owner,
            XamlElement e when offset > e.OpenNameSpan.End && IsInsideOpenTag(doc, e, offset) => e,
            _ => null,
        };
        if (attrOwner != null)
            return AttributeEntries(doc, attrOwner);

        // 3. Tag-name slot. Excluded for attribute / value nodes regardless of
        //    text context — a stray '<' inside a string is not a tag opener.
        if (node is not XamlAttribute && node is not XamlValue && PrecededByOpeningLess(doc, offset))
            return TagEntries(doc);

        return Array.Empty<XamlCompletionEntry>();
    }

    private IReadOnlyList<XamlCompletionEntry> TagEntries(XamlDocument doc) =>
        RootTypes(doc)
            .Select(t => new XamlCompletionEntry(
                t.Prefix.Length == 0 ? t.Name : $"{t.Prefix}:{t.Name}",
                XamlCompletionKind.Tag,
                "Xaml element"))
            .ToArray();

    private IReadOnlyList<XamlCompletionEntry> AttributeEntries(XamlDocument doc, XamlElement elem)
    {
        var type = ResolveElementType(doc, elem);
        if (type == null)
            return Array.Empty<XamlCompletionEntry>();
        return type.Properties
            .Select(p => new XamlCompletionEntry(p.Name, XamlCompletionKind.Attribute, p.Type))
            .ToArray();
    }

    private IReadOnlyList<XamlCompletionEntry> EnumValueEntries(
        XamlDocument doc, XamlElement elem, XamlAttribute attr)
    {
        var type = ResolveElementType(doc, elem);
        if (type == null)
            return Array.Empty<XamlCompletionEntry>();

        var name = doc.TextOf(attr.NameSpan);
        var prop = type.Properties.FirstOrDefault(p => p.Name == name);
        if (prop?.EnumValues == null)
            return Array.Empty<XamlCompletionEntry>();

        return prop.EnumValues
            .Select(v => new XamlCompletionEntry(v, XamlCompletionKind.EnumValue, "Enum value"))
            .ToArray();
    }

    // True if `offset` sits between the tag-name end and the open tag's '>'.
    // Scans forward from `offset`, skipping over attribute spans (whose values
    // may contain '<' or '>' as ordinary string content). First '>' encountered
    // outside attribute spans closes the open tag; a '<' first means we've
    // already crossed into the content area. EOF means the open tag was never
    // closed — treat the cursor as still inside it (tolerant input).
    private static Boolean IsInsideOpenTag(XamlDocument doc, XamlElement elem, Int32 offset)
    {
        var src = doc.Source;
        var i = offset;
        while (i < src.Length)
        {
            if (TrySkipAttribute(elem, ref i))
                continue;
            var c = src[i];
            if (c == '>') return true;
            if (c == '<') return false;
            i++;
        }
        return true;
    }

    private static Boolean TrySkipAttribute(XamlElement elem, ref Int32 i)
    {
        foreach (var a in elem.Attributes)
        {
            if (a.Span.Start <= i && i < a.Span.End)
            {
                i = a.Span.End;
                return true;
            }
        }
        return false;
    }

    private XamlType? ResolveElementType(XamlDocument doc, XamlElement elem)
    {
        var raw = doc.TextOf(elem.OpenNameSpan);
        var colon = raw.IndexOf(':');
        var prefix = colon < 0 ? String.Empty : raw.Substring(0, colon);
        var name = colon < 0 ? raw : raw.Substring(colon + 1);

        if (!doc.ClrPrefixes.TryGetValue(prefix, out var binding))
            return null;
        var asm = _resolver.GetAssembly(binding.asm);
        var t = asm?.GetType(binding.ns, name);
        return t == null ? null : new XamlType(prefix, t);
    }

    private static Boolean PrecededByOpeningLess(XamlDocument doc, Int32 offset)
    {
        var src = doc.Source;
        var i = offset;
        while (i > 0 && IsNameChar(src[i - 1]))
            i--;
        return i > 0 && src[i - 1] == '<';
    }

    private static Boolean IsNameChar(Char c) =>
        Char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '.' || c == '-';
}
