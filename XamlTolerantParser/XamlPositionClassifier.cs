using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

// Classifies a cursor position into a XamlPositionContext variant. Three-step
// algorithm:
//   1. If FindNodeAt lands inside a content node (value/comment/cdata),
//      classify by node type and stop — '<' has no syntactic meaning there.
//   2. Otherwise run a tag-name probe: walk back over name-chars; if
//      preceded by '<' or '</' it's a TagName / EndTagName slot.
//   3. Fall back to a switch on the FindNodeAt result.
//
// Hot path: a single FindNodeAt is performed up front and reused everywhere.
// Each downstream lookup (partial element under `<Butt|`, attribute with
// trailing `=`, element with matching close-tag name) is answered directly
// from that node — no extra tree descents and no whole-tree DFS.
internal static class XamlPositionClassifier
{
    public static XamlPositionContext Classify(XamlDocument doc, Int32 line, Int32 column)
    {
        // Trailing-edge rule: an element whose close tag is missing OWNS its
        // trailing edge — its half-open span ends at EOF and there is no next
        // construct for the boundary position to belong to (the same rule
        // XamlValue.FindNodeAt applies to an unclosed extension). Retry one
        // column back and accept only such an element. The OpenTagSpan guard
        // keeps an open tag that never reached '>' (`<Button |` at EOF) on
        // today's quiet Outside path — its edge is the attribute zone, not
        // content.
        var node = doc.FindNodeAt(line, column)
            ?? (column > 0 && doc.FindNodeAt(line, column - 1) is XamlElement
                { IsSelfClosing: false, CloseNameSpan: null, OpenTagSpan: not null } open
                ? open : null);
        var offset = doc.OffsetAt(line, column);
        var diagnostics = FilterDiagnostics(doc, line, column);

        // (1) Content nodes — '<' is just a character there.
        switch (node)
        {
            case XamlValue v: return MakeValueContext(v, diagnostics);
            case XamlComment c: return new CommentContext(c.OwnerElement, c, diagnostics);
            case XamlCData cd: return new CDataContext(cd.OwnerElement, cd, diagnostics);
            case XamlExtension ext: return ClassifyInExtension(doc, ext, offset, diagnostics);
            case XamlNamedArgument na: return ClassifyOnNamedArgument(na, offset, diagnostics);
            case XamlExtensionStringValue sv: return ClassifyOnStringValue(sv, diagnostics);
            case XamlPositionalArgument pa: return ClassifyOnPositionalArgument(pa, diagnostics);
        }

        // (2a) Cursor right after `=` of an attribute — value slot. Runs before
        //      the tag-name probe and the node switch because at end-of-source
        //      `<a b=` FindNodeAt returns the owning element (the attribute's
        //      Span ends at `=`, half-open), so ClassifyInElement would treat
        //      this as TagInterior without this branch.
        if (offset > 0 && doc.Source[offset - 1] == '=')
        {
            // `=` is at offset-1, inside the owning element's open tag. Try
            // cursorNode first; fall back to FindNodeAt at the `=` column when
            // the cursor sits past every span (EOF: `<a b=`).
            var hit = FindAttributeWithEqualsEndAt(node, offset);
            if (hit == null && column > 0)
                hit = FindAttributeWithEqualsEndAt(doc.FindNodeAt(line, column - 1), offset);
            if (hit.HasValue)
            {
                var (e, a) = hit.Value;
                return new AttributeValueContext(e, a, null, diagnostics);
            }
        }

        // (2b) Tag-name probe.
        var probe = ProbeTagName(doc.Source, offset);
        if (probe.Kind == ProbeKind.Open)
        {
            var nameStartOffset = offset - probe.NameLength;
            var elem = ResolveTagElement(doc, node, line, column, probe.NameLength,
                e => e.OpenNameSpan.Start == nameStartOffset);
            // Container is the element the '<' sits inside — the partial tag's
            // owner, or (when no partial node exists yet, e.g. `<a><|`) the
            // element the cursor is structurally in. Never `elem?.OwnerElement`
            // alone: that is null in the empty-name case and would misroute a
            // child position to the root. The column-1 retry covers the cursor
            // sitting at the enclosing element's exclusive End (`<a><|`), where
            // FindNodeAt at the cursor returns null but the '<' one column back
            // is inside the container.
            var container = elem != null
                ? elem.OwnerElement
                : EnclosingElement(node)
                  ?? (column > 0 ? EnclosingElement(doc.FindNodeAt(line, column - 1)) : null);
            return container == null
                ? new TagNameContext(elem, diagnostics)
                : new ChildTagNameContext(elem, container, diagnostics);
        }
        if (probe.Kind == ProbeKind.Close)
        {
            var nameStartOffset = offset - probe.NameLength;
            var elem = ResolveTagElement(doc, node, line, column, probe.NameLength,
                e => e.CloseNameSpan is { } cs && cs.Start == nameStartOffset);
            return new EndTagNameContext(elem, diagnostics);
        }

        // (2c) Attribute-name probe. A value-less attribute's span ends at its
        //      name, so a cursor at the name's exclusive End (`<Button Wi|`) —
        //      or past every span at EOF — misses the attribute node:
        //      FindNodeAt returns the owning element or null, which would
        //      misroute to TagInterior / Outside. When name-chars sit to the
        //      left (and it is not a tag name, handled by 2b above), retry one
        //      column back to recover the partial attribute.
        if (offset > 0 && IsNameChar(doc.Source[offset - 1]))
        {
            var attr = node as XamlAttribute
                ?? (column > 0 ? doc.FindNodeAt(line, column - 1) as XamlAttribute : null);
            if (attr != null)
                return new AttributeNameContext(attr.Owner!, attr, diagnostics);
        }

        // (3) Node-type switch.
        return node switch
        {
            null => new OutsideContext(diagnostics),
            XamlText t => new TextContext(t.OwnerElement, t, diagnostics),
            // Attribute invariant (parser-guaranteed): Owner is non-null.
            XamlAttribute a => new AttributeNameContext(a.Owner!, a, diagnostics),
            XamlElement e => ClassifyInElement(e, line, column, diagnostics),
            _ => new OutsideContext(diagnostics),
        };
    }

    private static AttributeValueContext MakeValueContext(XamlValue v, IReadOnlyList<XamlDiagnostic> diags)
    {
        // XamlValue.Parent is always its XamlAttribute (parser invariant).
        // XamlAttribute.Owner is always the owning XamlElement (parser invariant).
        var attr = (XamlAttribute)v.Parent!;
        return new AttributeValueContext(attr.Owner!, attr, v, diags);
    }

    // Interior vs content is a span fact the parser already recorded: inside
    // OpenTagSpan (`<name …>`) is the attribute area, past it is content. A
    // null OpenTagSpan means the open tag never reached its '>' (EOF /
    // recovery input) — the whole element is still the open tag, so the
    // cursor stays interior (tolerant input).
    private static XamlPositionContext ClassifyInElement(
        XamlElement e, Int32 line, Int32 column, IReadOnlyList<XamlDiagnostic> diags)
    {
        return e.OpenTagSpan is { } open && !open.Contains(line, column)
            ? new ElementContentContext(e, diags)
            : new TagInteriorContext(e, diags);
    }

    // Optimistically returns `cursorNode as XamlElement` when it satisfies
    // `match`; otherwise falls back to a single FindNodeAt at the name-start
    // position. Returns null when neither yields a matching element. The
    // fallback path is the EOF case (`<Butt` at end of source): the cursor
    // column is past every span's exclusive End, so FindNodeAt at the cursor
    // returned null even though the partial element exists in the tree.
    private static XamlElement? ResolveTagElement(
        XamlDocument doc, XamlNode? cursorNode, Int32 line, Int32 column,
        Int32 nameLength, Func<XamlElement, Boolean> match)
    {
        if (cursorNode is XamlElement direct && match(direct))
            return direct;
        var nameStartCol = column - nameLength;
        var elem = doc.FindNodeAt(line, nameStartCol) as XamlElement;
        return elem != null && match(elem) ? elem : null;
    }

    // The element a cursor node sits inside: the node itself when it is an
    // element, otherwise the nearest enclosing one. Used to find a child tag's
    // container when no partial element node exists yet (`<a><|`).
    private static XamlElement? EnclosingElement(XamlNode? node)
        => node as XamlElement ?? node?.OwnerElement;

    private enum ProbeKind { None, Open, Close }
    private readonly record struct ProbeResult(ProbeKind Kind, Int32 NameLength);

    // Pure offset-based scan over the source: walk back over name chars and
    // check whether the preceding char(s) form `<` or `</`. By construction
    // of the lexer the marker `<` cannot sit inside a XamlValue/Comment/CData
    // span (those don't contain `<` in their content), and step (1) has
    // already returned for cursors inside content nodes — so no defensive
    // tree lookup on the marker position is needed.
    private static ProbeResult ProbeTagName(String src, Int32 offset)
    {
        var i = offset;
        while (i > 0 && IsNameChar(src[i - 1])) i--;
        if (i == 0) return new(ProbeKind.None, 0);

        var nameLength = offset - i;
        if (src[i - 1] == '<')
            return new(ProbeKind.Open, nameLength);
        if (i >= 2 && src[i - 1] == '/' && src[i - 2] == '<')
            return new(ProbeKind.Close, nameLength);
        return new(ProbeKind.None, 0);
    }

    // Cursor is right after `=` of an attribute whose value has not been
    // emitted yet. Depending on where the parser placed the half-open
    // attribute span the cursor's deepest node is either the attribute
    // itself (its Span contains `=`) or the owning element (cursor at the
    // attribute's exclusive End). Both routes lead to the owning element's
    // attribute list.
    private static (XamlElement Element, XamlAttribute Attribute)? FindAttributeWithEqualsEndAt(
        XamlNode? cursorNode, Int32 offset)
    {
        var e = cursorNode switch
        {
            XamlElement el => el,
            XamlAttribute a => a.Owner,
            _ => null,
        };
        if (e == null) return null;
        foreach (var a in e.Attributes)
        {
            if (a.EqualsSpan is { } eq && eq.End == offset)
                return (e, a);
        }
        return null;
    }

    private static IReadOnlyList<XamlDiagnostic> FilterDiagnostics(XamlDocument doc, Int32 line, Int32 column)
    {
        List<XamlDiagnostic>? hits = null;
        foreach (var d in doc.Diagnostics)
        {
            if (d.Span.Contains(line, column))
            {
                hits ??= new List<XamlDiagnostic>();
                hits.Add(d);
            }
        }
        return (IReadOnlyList<XamlDiagnostic>?)hits ?? Array.Empty<XamlDiagnostic>();
    }

    private static Boolean IsNameChar(Char c) =>
        Char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';

    // -------- markup extension classification --------

    // Cursor landed on a XamlExtension node — the immediately-enclosing markup
    // extension, with the caret in its TypeName or in a gap of the argument area
    // (whitespace, or just past a half-open argument span). Classifies the slot
    // from STRUCTURE, never from the caret's exact adjacency to a token — so
    // whitespace around the value or the '=' does not change the answer.
    private static XamlPositionContext ClassifyInExtension(
        XamlDocument doc, XamlExtension ext, Int32 offset, IReadOnlyList<XamlDiagnostic> diags)
    {
        var attr = FindOwningAttribute(ext);
        // Extension always lives under a XamlAttribute by construction.
        var element = attr.Owner!;

        // (i) Inside or immediately after the type name. For an empty type name
        //     (`{|`) the only valid offset is right after '{'.
        var typeEnd = ext.TypeNameSpan.Length > 0
            ? ext.TypeNameSpan.End
            : ext.OpenBraceSpan.End;
        if (offset > ext.OpenBraceSpan.Start && offset <= typeEnd)
            return new ExtensionTypeNameContext(element, attr, ext, diags);

        // (ii) Past the type name the caret is in the argument area. Decide the
        //      slot by the governing separator to its LEFT, found structurally by
        //      skipping the token under the caret and the whitespace around it
        //      (so a stray space — `{Bind Type=D |}`, `{Bind Type= |}` — never
        //      changes the answer; it is the *meaning* "which side of the '='",
        //      not the caret touching a token). The scan stops at the open brace,
        //      so `p` always lands at a defined boundary. Total by construction —
        //      exactly two outcomes, both described:
        var src = doc.Source;
        var lo = ext.OpenBraceSpan.End;
        var p = offset;
        while (p > lo && Char.IsWhiteSpace(src[p - 1])) p--;
        while (p > lo && IsNameChar(src[p - 1])) p--;
        while (p > lo && Char.IsWhiteSpace(src[p - 1])) p--;

        //   (a) a '=' to the left that a parsed named argument owns → we are in
        //       that argument's value: offer values.
        if (p > 0 && src[p - 1] == '=')
            foreach (var a in ext.Arguments)
                if (a is XamlNamedArgument na && na.EqualsSpan is { } eq && eq.End == p)
                    return new ExtensionArgValueContext(element, attr, ext, na, na.Value, diags);

        //   (b) everything else — no '=' (a ',' starting a new argument, the '{'
        //       or type name with no argument yet), OR a '=' the parser produced no
        //       named argument for (malformed, e.g. a '=' inside a positional
        //       token): the name/interior slot, offering the argument names. This
        //       is the deliberate catch-all, not an accidental fall-through.
        return new ExtensionInteriorContext(element, attr, ext, diags);
    }

    private static XamlPositionContext ClassifyOnNamedArgument(
        XamlNamedArgument na, Int32 offset, IReadOnlyList<XamlDiagnostic> diags)
    {
        var ext = (XamlExtension)na.Parent!;
        var attr = FindOwningAttribute(ext);
        var element = attr.Owner!;

        // Past the '=' but before the (possibly null) value's span — value slot.
        if (na.EqualsSpan is { } eq && offset >= eq.End)
            return new ExtensionArgValueContext(element, attr, ext, na, na.Value, diags);

        return new ExtensionArgNameContext(element, attr, ext, na, diags);
    }

    private static XamlPositionContext ClassifyOnPositionalArgument(
        XamlPositionalArgument pa, IReadOnlyList<XamlDiagnostic> diags)
    {
        var ext = (XamlExtension)pa.Parent!;
        var attr = FindOwningAttribute(ext);
        var element = attr.Owner!;
        return new ExtensionPositionalArgContext(element, attr, ext, pa, diags);
    }

    private static XamlPositionContext ClassifyOnStringValue(
        XamlExtensionStringValue sv, IReadOnlyList<XamlDiagnostic> diags)
    {
        var arg = (XamlExtensionArgument)sv.Parent!;
        var ext = (XamlExtension)arg.Parent!;
        var attr = FindOwningAttribute(ext);
        var element = attr.Owner!;
        // The argument kind decides the variant — the classifier owns this
        // discrimination so consumers never inspect Argument.
        return arg is XamlPositionalArgument pa
            ? new ExtensionPositionalArgContext(element, attr, ext, pa, diags)
            : new ExtensionArgValueContext(element, attr, ext, (XamlNamedArgument)arg, sv, diags);
    }

    // Walks the Parent chain past any number of nested-extension layers
    // until the owning XamlAttribute is reached. Parser invariant guarantees
    // every extension descendant has one.
    private static XamlAttribute FindOwningAttribute(XamlNode node)
    {
        var p = node.Parent;
        while (p != null)
        {
            if (p is XamlAttribute a) return a;
            p = p.Parent;
        }
        throw new InvalidOperationException("Extension node has no XamlAttribute ancestor.");
    }
}
