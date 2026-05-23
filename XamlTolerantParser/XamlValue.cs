using System;

namespace XamlTolerantParser;

public sealed class XamlValue : XamlNode
{
    public TextSpan ContentSpan { get; }
    public Char Quote { get; }
    public Boolean IsUnterminated { get; }

    public XamlValue(TextSpan span, TextSpan contentSpan, Char quote, Boolean isUnterminated)
        : base(span)
    {
        ContentSpan = contentSpan;
        Quote = quote;
        IsUnterminated = isUnterminated;
    }
}
