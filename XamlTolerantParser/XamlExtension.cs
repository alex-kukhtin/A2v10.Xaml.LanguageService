using System.Collections.Generic;

namespace XamlTolerantParser;

// Markup extension: '{' TypeName (Arg (',' Arg)*)? '}'.
//
// Inherits XamlExtensionValue so a XamlExtension can sit directly as the
// Value of another XamlExtensionArgument — nesting (`{Binding Source=
// {StaticResource Foo}}`) needs no separate wrapper.
//
// TypeNameSpan has Length == 0 when the user just typed '{' with nothing
// after it (a real edit-time state we still want to represent for completion).
// CloseBraceSpan is null when the closing '}' is missing.
public sealed class XamlExtension : XamlExtensionValue
{
    public TextSpan OpenBraceSpan { get; }
    public TextSpan TypeNameSpan { get; internal set; }
    public TextSpan? CloseBraceSpan { get; internal set; }
    public IList<XamlExtensionArgument> Arguments { get; } = new List<XamlExtensionArgument>();

    public bool IsClosed => CloseBraceSpan.HasValue;

    public XamlExtension(TextSpan openBraceSpan) : base(openBraceSpan)
    {
        OpenBraceSpan = openBraceSpan;
    }

    public override XamlNode FindNodeAt(int line, int column)
    {
        if (Arguments is IReadOnlyList<XamlExtensionArgument> args && args.Count > 0)
        {
            var i = SpanSearch.FindContaining(args, line, column, static a => a.Span);
            if (i >= 0)
                return args[i].FindNodeAt(line, column);
        }
        return this;
    }
}
