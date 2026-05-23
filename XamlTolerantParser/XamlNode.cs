namespace XamlTolerantParser;

public abstract class XamlNode
{
    public TextSpan Span { get; internal set; }
    public XamlNode? Parent { get; internal set; }

    protected XamlNode(TextSpan span)
    {
        Span = span;
    }
}
