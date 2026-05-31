using System;

namespace XamlTolerantParser;

// Computes the on-type auto-edit for a freshly typed trigger character, the
// mirror of XamlPositionClassifier for formatting. Invoked from
// XamlDocument.EditAt; the server feeds it the LSP onTypeFormatting trigger.
//
// Key difference from ContextAt: onTypeFormatting fires AFTER the character is
// already in the buffer, with the caret right after it. At that caret the
// element's span has its exclusive End (EOF cases like "<Button>" / "<Button/"),
// so FindNodeAt(caret) returns null and ContextAt would say Outside. The robust
// primitive here is FindNodeAt at the *typed character* (column - 1): that
// position is inside the element's span, so it yields the element directly.
internal static class XamlTypingEditor
{
    public static XamlTypingEdit? Edit(XamlDocument doc, Int32 line, Int32 column, Char ch) => ch switch
    {
        '>' => CloseTag(doc, line, column),
        '/' => SelfClose(doc, line, column),
        '"' => ClosePair(doc, line, column, '"'),
        '\'' => ClosePair(doc, line, column, '\''),
        '`' => ClosePair(doc, line, column, '`'),
        '{' => ClosePair(doc, line, column, '}'),
        _ => null,
    };

    // '>' was just typed. If it terminates the open tag of an element that is
    // neither self-closing nor already closed, insert the matching </name> at the
    // caret (empty Replace => caret stays between the tags).
    //
    // Guards rely on FindNodeAt at the '>' position attributing it to the element
    // itself: a '>' inside an attribute value or text content resolves to a
    // XamlValue / XamlText instead, so those never reach the insert.
    private static XamlTypingEdit? CloseTag(XamlDocument doc, Int32 line, Int32 column)
    {
        if (column == 0) return null;
        var offset = doc.OffsetAt(line, column);
        if (offset == 0 || doc.Source[offset - 1] != '>') return null;

        if (doc.FindNodeAt(line, column - 1) is not XamlElement e) return null;
        if (e.IsSelfClosing || e.CloseNameSpan is not null) return null;

        var name = doc.TextOf(e.OpenNameSpan);
        if (name.Length == 0) return null; // "<>" — no name to mirror

        var caret = new TextSpan(offset, 0, line, column, line, column);
        return new XamlTypingEdit(caret, $"</{name}>");
    }

    // Auto-closing pair: the user typed an opening character ('"', '\'', '`', '{');
    // insert its closing counterpart at the caret. Empty Replace at the caret =>
    // the caret stays to the LEFT, i.e. between the pair (`{|}`, `"|"`). This is
    // dumb pair-completion, exactly like an editor's auto-closing-pairs: no context
    // analysis, no guards — it fires anywhere, regardless of tag / value / text.
    private static XamlTypingEdit ClosePair(XamlDocument doc, Int32 line, Int32 column, Char close)
    {
        var offset = doc.OffsetAt(line, column);
        var caret = new TextSpan(offset, 0, line, column, line, column);
        return new XamlTypingEdit(caret, close.ToString());
    }

    // '/' was just typed inside an open tag. Complete it to '/>' by replacing the
    // '/' (Replace range ends at the caret => caret lands after the inserted '>').
    private static XamlTypingEdit? SelfClose(XamlDocument doc, Int32 line, Int32 column)
    {
        if (column == 0) return null;
        var offset = doc.OffsetAt(line, column);
        if (offset == 0 || doc.Source[offset - 1] != '/') return null;

        // Already '/>': the user typed '/' just before an existing '>'. The element
        // is now self-closing; if a matching close tag stands LITERALLY right after
        // the '>' it is now redundant — collapse it. Otherwise nothing to do.
        if (offset < doc.Source.Length && doc.Source[offset] == '>')
            return CollapseRedundantCloseTag(doc, line, column, offset);

        // Must be the self-close slash of an open tag, not a '/' in a value/text.
        if (doc.FindNodeAt(line, column - 1) is not XamlElement) return null;

        var slash = new TextSpan(offset - 1, 1, line, column - 1, line, column);
        return new XamlTypingEdit(slash, "/>");
    }

    // The user self-closed an element that still carries a close tag: `<Grid/></Grid>`
    // with the caret between '/' and '>'. Delete the close tag iff it stands literally
    // right after the open tag's '>' and its name matches the element — the typical
    // `<Grid|></Grid>` keystroke. Deliberately strict: no whitespace skipping, no
    // content analysis (adjacency already implies the element is empty). The name
    // match is mandatory — a partial child before a parent's close tag
    // (`<StackPanel><Grid/></StackPanel>`) must NOT eat the parent's `</StackPanel>`.
    // The deletion is entirely to the right of the caret, so the caret stays put.
    private static XamlTypingEdit? CollapseRedundantCloseTag(
        XamlDocument doc, Int32 line, Int32 column, Int32 gtOffset)
    {
        if (doc.FindNodeAt(line, column - 1) is not XamlElement e) return null;
        var name = doc.TextOf(e.OpenNameSpan);
        if (name.Length == 0) return null;

        // Literal "</name>" immediately after the '>' (which sits at gtOffset).
        var expected = "</" + name + ">";
        var start = gtOffset + 1;
        var src = doc.Source;
        if (start + expected.Length > src.Length) return null;
        if (string.CompareOrdinal(src, start, expected, 0, expected.Length) != 0) return null;

        // Replace the close tag with nothing. Its span is on a single line by
        // construction (no '<' / '>' / newline inside "</name>"). The '>' sits at
        // the caret column, so the close tag starts one column further right.
        var span = new TextSpan(
            start, expected.Length,
            line, column + 1,
            line, column + 1 + expected.Length);
        return new XamlTypingEdit(span, "");
    }
}
