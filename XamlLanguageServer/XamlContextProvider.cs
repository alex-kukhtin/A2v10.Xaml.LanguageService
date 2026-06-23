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
    // Building block: every public type reachable through the document's
    // clr-namespace prefixes, each paired with its binding prefix. No role
    // filtering here — entry builders apply their own (root tags want
    // IsRootElement, extension names want IsMarkupExtension, etc.). Consumed by
    // the entry builders and by tests; handlers should call Complete instead.
    public IEnumerable<FullXamlType> ReachableTypes(XamlDocument doc)
    {
        foreach (var (prefix, (asmName, ns)) in doc.ClrPrefixes)
        {
            var asm = _resolver.GetAssembly(asmName);
            if (asm == null)
                continue;
            foreach (var t in asm.GetTypes(ns))
                yield return new FullXamlType(t, prefix);
        }
    }

    // The single position-aware entry point. Classifies the cursor with
    // ContextAt (the parser owns all position semantics — see CLAUDE.md
    // "Position classification") and dispatches on the resulting context
    // variant. Empty list = nothing to suggest. Position is LSP-shaped
    // (0-based line, 0-based UTF-16 column).
    //
    // Arm → offering:
    //   TagName                          → root element tags (+ comment / CDATA)
    //   ChildTagName                     → child element tags (+ comment / CDATA)
    //   ElementContent                   → child element tags of the container
    //   AttributeName                    → settable properties of the element
    //   ExtensionTypeName                → markup-extension types
    //   ExtensionInterior / ArgName /    → the extension type's properties
    //     PositionalArg                    (every "argument name" slot)
    //   AttributeValue / ExtensionArgValue → the property's enum members, or
    //     (named-arg value)                  True/False for a Boolean property.
    // TagInterior is intentionally empty — the empty interior slot stays quiet;
    // typing an attribute name routes to AttributeName instead (see CLAUDE.md
    // "Position classification", the attribute-name probe).
    // Outside / Text / Comment / CData / EndTagName offer nothing (the `_` arm).
    public XamlCompletionResult Complete(XamlDocument doc, Int32 line, Int32 column)
    {
        var ctx = doc.ContextAt(line, column);
        var entries = ctx switch
        {
            TagNameContext => WithSyntax(RootEntries(doc)),
            ChildTagNameContext ctn => ContentEntries(doc, ctn.Container, offerSyntax: true),
            ElementContentContext ecc => ContentEntries(doc, ecc.Element, offerSyntax: false),
            TagInteriorContext => [],
            AttributeNameContext anc => [.. PropertyEntries(ResolveType(doc, anc.Element)), .. AttachedEntries(doc, anc.Element)],
            AttributeValueContext avc => ValueEntries(AttributeValueProperty(doc, avc.Element, doc.TextOf(avc.Attribute.NameSpan))),
            ExtensionTypeNameContext => ExtensionTypeEntries(doc),
            ExtensionInteriorContext eic => ExtensionPropertyEntries(doc, eic.Extension),
            ExtensionArgNameContext ean => ExtensionPropertyEntries(doc, ean.Extension),
            ExtensionPositionalArgContext epc => ExtensionPropertyEntries(doc, epc.Extension),
            ExtensionArgValueContext eav => ValueEntries(
                PropertyByName(ResolveType(doc, doc.TextOf(eav.Extension.TypeNameSpan)), doc.TextOf(eav.Argument.NameSpan))),
            _ => [],
        };
        // The replaced range is the identifier run around the caret — one text
        // fact, the same in every context (see XamlDocument.IdentifierSpanAt).
        // The handler stamps it onto each item's TextEdit so the editor never
        // guesses the range itself (which ate the quotes in `Disabled="|"` and
        // duplicated the token in `{Bind Dat|}`).
        //
        // When the attribute value has no closing quote yet (the `Background="|`
        // race state — see IsUnterminated), append the matching quote so the
        // committed value stays balanced regardless of what VS does with its
        // own applicable span.
        var suffix = ctx is AttributeValueContext { Value: { IsUnterminated: true } v }
            ? v.Quote.ToString()
            : "";
        return new XamlCompletionResult(entries, doc.IdentifierSpanAt(line, column), suffix);
    }

    // Point lookup: resolve one element to its bound type — the inverse of
    // ReachableTypes' enumeration. The name comes from OpenNameSpan; the
    // qualified-name overload below does the prefix work.
    public FullXamlType? ResolveType(XamlDocument doc, XamlElement element)
        => ResolveType(doc, doc.TextOf(element.OpenNameSpan));

    // Resolve a qualified type name (`my:Grid`, or bare `Grid` for the default
    // binding): split on ':', map the prefix through the document's ClrPrefixes
    // to (assembly, namespace), and ask the resolver. Returns null when the
    // prefix is unbound, the assembly is missing from @assemblies, or the type
    // is not found.
    public FullXamlType? ResolveType(XamlDocument doc, String qualified)
    {
        var colon = qualified.IndexOf(':');
        var prefix = colon < 0 ? String.Empty : qualified[..colon];
        var localName = colon < 0 ? qualified : qualified[(colon + 1)..];

        if (!doc.ClrPrefixes.TryGetValue(prefix, out var coords))
            return null;

        var asm = _resolver.GetAssembly(coords.asm);
        var type = asm?.GetType(coords.ns, localName);
        return type == null ? null : new FullXamlType(type, prefix);
    }

    private static XamlCompletionEntry CreateCompletionTag(FullXamlType xt)
        => new(xt.QualifiedName, XamlCompletionKind.Tag, "Xaml element");

    // Syntactic constructs offered alongside element tags after `<`: an XML
    // comment and a CDATA section. The label is what VS filters/displays (`!--`);
    // InsertText carries the full body MINUS the leading `<` (already typed, and
    // not part of the replaced identifier span). SnippetSupport is false (see
    // XamlLangServer), so there is no caret placeholder — after commit the caret
    // lands at the end of the inserted text.
    private static readonly XamlCompletionEntry[] _syntaxEntries =
    [
        new("!--", XamlCompletionKind.Snippet, "Comment", "!--  -->"),
        new("![CDATA[", XamlCompletionKind.Snippet, "CDATA section", "![CDATA[]]>"),
    ];

    // Element tags plus the syntactic constructs (comment, CDATA), for a tag-name
    // slot where `<` has been typed: the root slot and the normal child slot.
    // Callers gate it (ElementContent and property elements pass through their own
    // un-wrapped path) so the snippets land only where a leading `<` is in the
    // buffer and free markup is actually valid.
    private static IReadOnlyList<XamlCompletionEntry> WithSyntax(IReadOnlyList<XamlCompletionEntry> tags)
        => [.. tags, .. _syntaxEntries];

    // Tags valid as a document root — types deriving from RootContainer.
    // Label is prefix-qualified (`my:Page`) for a non-default binding. We
    // enumerate the full candidate set rather than point-look-up the partial
    // name under the cursor: the editor filters as the user types, and looking
    // up an in-progress name would just miss — see project_completion_no_partial_lookup.
    private IReadOnlyList<XamlCompletionEntry> RootEntries(XamlDocument doc)
    {
        return [.. ReachableTypes(doc)
            .Where(x => x.Type.IsRootElement)
            .Select(CreateCompletionTag)];
    }

    private IReadOnlyList<XamlCompletionEntry> ExtensionTypeEntries(XamlDocument doc)
    {
        return [.. ReachableTypes(doc)
            .Where(x => x.Type.IsMarkupExtension)
            .Select(CreateCompletionTag)];
    }

    // offerSyntax appends the comment / CDATA snippets — true only for the child
    // TAG slot (`<` already typed). The bare ElementContent slot (no `<` in the
    // buffer) passes false: its tag bodies carry no leading `<`, and a comment
    // body must, so the two cannot share an InsertText. Property elements never
    // get the snippets either (handled by the early return below).
    private IReadOnlyList<XamlCompletionEntry> ContentEntries(
        XamlDocument doc, XamlElement container, Boolean offerSyntax)
    {
        var name = doc.TextOf(container.OpenNameSpan);

        // Property element <Owner.Property> — its children are the items of that
        // property's collection. Split on the first dot; the owner part may
        // itself be prefix-qualified (my:Table.Header). No comment / CDATA here:
        // a property element's content is its property value, not free markup.
        var dot = name.IndexOf('.');
        if (dot >= 0)
            return PropertyElementEntries(doc, name[..dot], name[(dot + 1)..]);

        var containerType = ResolveType(doc, container);

        Boolean IsVisible(FullXamlType f)
        {
            var t = f.Type;
            // Never valid as a child element, whatever the container is.
            if (t.IsRootElement || t.IsMarkupExtension)
                return false;
            // A typed content model is authoritative: the container's element
            // type decides, and the coarse IsUiElement role filter steps aside
            // (a typed child like TableRow need not itself be a UIElement).
            if (containerType != null && containerType.Type.HasTypedContent)
                return containerType.Type.IsVisibleIn(t);
            // Unconstrained model — fall back to the coarse role filter, still
            // honouring the container's convention (Inlines → IsInline).
            return t.IsUiElement
                && (containerType == null || containerType.Type.IsVisibleIn(t));
        }

        var tags = ReachableTypes(doc)
            .Where(IsVisible)
            .Select(CreateCompletionTag);
        return offerSyntax ? WithSyntax([.. tags]) : [.. tags];
    }

    // Children inside a property element <Owner.Property>: valid only when
    // Property is a collection (XamlProperty.ContentElementType != null) — its
    // item type constrains the children exactly like a typed content model.
    // Scalar property elements (<Button.Content>) and unknown owners/properties
    // offer nothing for now (see CLAUDE.md "What still needs to be built").
    private IReadOnlyList<XamlCompletionEntry> PropertyElementEntries(
        XamlDocument doc, String ownerName, String propertyName)
    {
        var owner = ResolveType(doc, ownerName);
        var elementType = owner?.Type.Properties
            .FirstOrDefault(p => p.Name == propertyName)?.ContentElementType;
        if (elementType == null)
            return [];

        return [.. ReachableTypes(doc)
            .Where(f => f.Type.IsAChild(elementType))
            .Select(CreateCompletionTag)];
    }

    // Settable properties of a resolved type, as attribute-name completion
    // entries. Shared by the attribute-name slot (`<Button Wi|`) and the
    // markup-extension argument-name slots (`{Binding |}`, `{Binding Pa|th}`):
    // all three offer the owner type's properties and differ only in how the
    // owner is resolved (element name vs extension type name). Null owner —
    // unbound prefix, missing assembly, unknown type — offers nothing.
    private static IReadOnlyList<XamlCompletionEntry> PropertyEntries(FullXamlType? owner)
    {
        if (owner == null)
            return [];
        return [.. owner.Type.Properties
            .Select(p => new XamlCompletionEntry(p.Name, XamlCompletionKind.Attribute, "Xaml Property"))];
    }

    // Attached properties an element may carry, contributed by its ancestor
    // containers (a child of <Grid> may set Grid.Row). Walk up the Parent chain:
    // the direct container always contributes its own attached names; we keep
    // climbing only THROUGH an attached-transparent ancestor (IsAttachedTransparent),
    // so an outer container's attached reach a deeper descendant only when the
    // levels between are transparent — default false means direct children only,
    // the WPF rule. Labels are owner-qualified (`my:Grid.Row`); deduped by label
    // because two same-type transparent ancestors would name the same attached.
    private IReadOnlyList<XamlCompletionEntry> AttachedEntries(XamlDocument doc, XamlElement element)
    {
        var seen = new HashSet<String>(StringComparer.Ordinal);
        var entries = new List<XamlCompletionEntry>();
        for (var current = element.Parent as XamlElement; current != null; current = current.Parent as XamlElement)
        {
            var owner = ResolveType(doc, current);
            if (owner == null)
                break; // unknown ancestor: cannot read its attached or transparency
            foreach (var attached in owner.Type.AttachedProperties)
            {
                var label = $"{owner.QualifiedName}.{attached.Name}";
                if (seen.Add(label))
                    entries.Add(new XamlCompletionEntry(label, XamlCompletionKind.Attribute, "Xaml Property"));
            }
            if (!owner.Type.IsAttachedTransparent)
                break;
        }
        return entries;
    }

    // Properties of the type named by a markup extension (`{Binding |}` →
    // Binding's properties), resolved through the extension's TypeNameSpan.
    private IReadOnlyList<XamlCompletionEntry> ExtensionPropertyEntries(XamlDocument doc, XamlExtension ext)
        => PropertyEntries(ResolveType(doc, doc.TextOf(ext.TypeNameSpan)));

    private static readonly String[] _boolValues = ["True", "False"];

    // Values offered in a value slot (`<Button Visibility="|"`, `{Binding
    // Mode=|}`, `<Block Grid.VAlign="|"`): the enum members of the resolved
    // property, or True/False when it is a Boolean. All slots share this, differing
    // only in how the property is resolved (helpers below). Nullable enums / bools
    // are already unwrapped at index time, so a Boolean? / SomeEnum? lands here
    // exactly like its non-null form. A null property, or one that is neither enum
    // nor bool (string, number, type-converter values not yet wired) offers nothing.
    private static IReadOnlyList<XamlCompletionEntry> ValueEntries(XamlProperty? prop)
    {
        var values = prop?.EnumValues ?? (prop?.Type == "Boolean" ? _boolValues : null);
        if (values == null)
            return [];

        return [.. values.Select(v => new XamlCompletionEntry(v, XamlCompletionKind.EnumValue, null))];
    }

    private static XamlProperty? PropertyByName(FullXamlType? owner, String name)
        => owner?.Type.Properties.FirstOrDefault(p => p.Name == name);

    // The property behind an attribute-value slot. A dotted name is an attached
    // property: the owner is the type named by the dotted prefix (`Grid` in
    // `Grid.VAlign`, prefix-qualified `my:Grid` handled by ResolveType), and the
    // property lives in that type's AttachedProperties — the same owner-from-name
    // split the property-element path uses. A plain name is the element's own
    // property.
    private XamlProperty? AttributeValueProperty(XamlDocument doc, XamlElement element, String attrName)
    {
        var dot = attrName.IndexOf('.');
        if (dot < 0)
            return PropertyByName(ResolveType(doc, element), attrName);

        var owner = ResolveType(doc, attrName[..dot]);
        var propertyName = attrName[(dot + 1)..];
        return owner?.Type.AttachedProperties.FirstOrDefault(p => p.Name == propertyName);
    }
}
