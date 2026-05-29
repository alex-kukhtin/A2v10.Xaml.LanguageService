namespace XamlTolerantParser;

// Verbatim text value of a markup extension argument: the bare span between
// '=' (or '{TypeName ') and the next ',' / '}'. No quoting, no escapes
// processed — consumers read TextOf(Span) and interpret as needed.
public sealed class XamlExtensionStringValue : XamlExtensionValue
{
    public XamlExtensionStringValue(TextSpan span) : base(span) { }
}
