namespace XamlTolerantParser;

public readonly record struct Token(TokenKind Kind, TextSpan Span, bool IsUnterminated = false)
{
    public override string ToString() =>
        IsUnterminated ? $"{Kind}{Span} (unterminated)" : $"{Kind}{Span}";
}
