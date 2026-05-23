namespace XamlTolerantParser;

public sealed class XamlDiagnostic
{
    public TextSpan Span { get; }
    public string Message { get; }

    public XamlDiagnostic(TextSpan span, string message)
    {
        Span = span;
        Message = message;
    }

    public override string ToString() => $"{Span} {Message}";
}
