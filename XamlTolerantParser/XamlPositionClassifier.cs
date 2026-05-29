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
        var node = doc.FindNodeAt(line, column);
        var offset = doc.OffsetAt(line, column);
        var diagnostics = FilterDiagnostics(doc, line, column);

        // (1) Content nodes — '<' is just a character there.
        switch (node)
        {
            case XamlValue v: return MakeValueContext(v, diagnostics);
            case XamlComment c: return new CommentContext(c.OwnerElement, c, diagnostics);
            case XamlCData cd: return new CDataContext(cd.OwnerElement, cd, diagnostics);
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
            return new TagNameContext(elem, elem?.OwnerElement, diagnostics);
        }
        if (probe.Kind == ProbeKind.Close)
        {
            var nameStartOffset = offset - probe.NameLength;
            var elem = ResolveTagElement(doc, node, line, column, probe.NameLength,
                e => e.CloseNameSpan is { } cs && cs.Start == nameStartOffset);
            return new EndTagNameContext(elem, diagnostics);
        }

        // (3) Node-type switch.
        return node switch
        {
            null => new OutsideContext(diagnostics),
            XamlText t => new TextContext(t.OwnerElement, t, diagnostics),
            // Attribute invariant (parser-guaranteed): Owner is non-null.
            XamlAttribute a => new AttributeNameContext(a.Owner!, a, diagnostics),
            XamlElement e => ClassifyInElement(doc, e, offset, diagnostics),
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

    private static XamlPositionContext ClassifyInElement(
        XamlDocument doc, XamlElement e, Int32 offset, IReadOnlyList<XamlDiagnostic> diags)
    {
        return IsInsideOpenTag(doc.Source, e, offset)
            ? new TagInteriorContext(e, diags)
            : new ElementContentContext(e, diags);
    }

    // True when `offset` sits between the open tag's name end and its closing
    // '>' or '/>'. Scans forward, skipping attribute spans (whose values may
    // contain raw '<' / '>'). EOF means the open tag was never closed — treat
    // the cursor as still inside (tolerant input).
    private static Boolean IsInsideOpenTag(String src, XamlElement elem, Int32 offset)
    {
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
}
