using System;

namespace XamlTolerantParser;

public sealed class XamlValue : XamlNode
{
    public TextSpan ContentSpan { get; }
    public Char Quote { get; }
    public Boolean IsUnterminated { get; }

    // Parsed markup extension when ContentSpan opens with '{' (and not the
    // '{}' escape). Null for plain string values. Lives entirely inside
    // ContentSpan; the XamlValue.Span still covers the whole quoted region.
    public XamlExtension? Extension { get; internal set; }

    public XamlValue(TextSpan span, TextSpan contentSpan, Char quote, Boolean isUnterminated)
        : base(span)
    {
        ContentSpan = contentSpan;
        Quote = quote;
        IsUnterminated = isUnterminated;
    }

    public override XamlNode FindNodeAt(int line, int column)
    {
        if (Extension is { } ext)
        {
            // Normal containment, OR cursor sits exactly at the trailing edge
            // of an unclosed extension (`"{|"`): the '}' that would have moved
            // the End column past the cursor never arrived. Descending there
            // is what the user means — they are typing into the extension.
            if (ext.Span.Contains(line, column)
                || (!ext.IsClosed && ext.Span.EndLine == line && ext.Span.EndColumn == column))
                return ext.FindNodeAt(line, column);
        }
        return this;
    }
}
