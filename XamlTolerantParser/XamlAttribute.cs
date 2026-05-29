namespace XamlTolerantParser;

public sealed class XamlAttribute : XamlNode
{
    public TextSpan NameSpan { get; }
    public TextSpan? EqualsSpan { get; internal set; }
    public XamlValue? Value { get; internal set; }

    // Typed shortcut: an attribute's parent is always an element by parser
    // invariant. Avoids the `Parent as XamlElement` cast at every call site.
    public XamlElement? Owner => Parent as XamlElement;

    public XamlAttribute(TextSpan nameSpan) : base(nameSpan)
    {
        NameSpan = nameSpan;
    }

    public override XamlNode FindNodeAt(int line, int column)
    {
        if (Value is { } v && v.Span.Contains(line, column))
            return v;
        return this;
    }
}
