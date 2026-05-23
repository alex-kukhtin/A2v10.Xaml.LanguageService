namespace XamlTolerantParser;

public sealed class XamlAttribute : XamlNode
{
    public TextSpan NameSpan { get; }
    public TextSpan? EqualsSpan { get; internal set; }
    public XamlValue? Value { get; internal set; }

    public XamlAttribute(TextSpan nameSpan) : base(nameSpan)
    {
        NameSpan = nameSpan;
    }
}
