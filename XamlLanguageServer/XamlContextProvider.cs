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
    // Skeleton step: every arm returns nothing for now. Each will be filled
    // in turn:
    //   TagName / ElementContent      → root / child element tags
    //   TagInterior / AttributeName   → settable properties of the element
    //   AttributeValue                → enum / bool values
    //   ExtensionTypeName             → markup-extension types
    //   ExtensionInterior / ArgName   → extension-type properties (named args)
    //   ExtensionArgValue             → enum / bool values
    // Outside / Text / Comment / CData / EndTagName offer nothing (the `_` arm).
    public IReadOnlyList<XamlCompletionEntry> Complete(XamlDocument doc, Int32 line, Int32 column)
    {
        var ctx = doc.ContextAt(line, column);
        return ctx switch
        {
            TagNameContext             => RootEntries(doc, ctx),
            ElementContentContext      => ContentEntries(doc, ctx),
            TagInteriorContext         => [],
            AttributeNameContext       => [],
            AttributeValueContext      => [],
            ExtensionTypeNameContext   => [],
            ExtensionInteriorContext   => [],
            ExtensionArgNameContext    => [],
            ExtensionArgValueContext   => [],
            _                          => [],
        };
    }

    private static XamlCompletionEntry CreateCompletionTag(FullXamlType xt)
        => new XamlCompletionEntry(xt.QualifiedName, XamlCompletionKind.Tag, "Xaml element");

    // Tags valid as a document root — types deriving from RootContainer.
    // Label is prefix-qualified (`my:Page`) for a non-default binding. We
    // enumerate the full candidate set rather than point-look-up the partial
    // name under the cursor: the editor filters as the user types, and looking
    // up an in-progress name would just miss — see project_completion_no_partial_lookup.
    private IReadOnlyList<XamlCompletionEntry> RootEntries(XamlDocument doc, XamlPositionContext ctx)
    {
        var rt = ReachableTypes(doc).ToList();
        return ReachableTypes(doc)
            .Where(x => x.Type.IsRootElement)
            .Select(CreateCompletionTag)
            .ToList();
    }

    private IReadOnlyList<XamlCompletionEntry> ContentEntries(XamlDocument doc, XamlPositionContext ctx)
    {
        return ReachableTypes(doc)
            .Where(x => !x.Type.IsMarkupExtension && !x.Type.IsRootElement)
            .Select(CreateCompletionTag)
            .ToList();
    }
}
