namespace XamlTolerantParser;

// One argument inside a markup extension's argument list. Two shapes:
//   * XamlPositionalArgument — a bare value
//   * XamlNamedArgument      — Name '=' Value
// Value can be null when '=' is present but no value was supplied
// (`{Binding Path=}` or `{Binding Path=, Mode=OneWay}`).
public abstract class XamlExtensionArgument : XamlNode
{
    public XamlExtensionValue? Value { get; internal set; }

    protected XamlExtensionArgument(TextSpan span) : base(span) { }

    public override XamlNode FindNodeAt(int line, int column)
    {
        if (Value is { } v && v.Span.Contains(line, column))
            return v.FindNodeAt(line, column);
        return this;
    }
}

public sealed class XamlPositionalArgument : XamlExtensionArgument
{
    public XamlPositionalArgument(TextSpan span) : base(span) { }
}

public sealed class XamlNamedArgument : XamlExtensionArgument
{
    public TextSpan NameSpan { get; }
    public TextSpan? EqualsSpan { get; internal set; }

    public XamlNamedArgument(TextSpan nameSpan) : base(nameSpan)
    {
        NameSpan = nameSpan;
    }
}
