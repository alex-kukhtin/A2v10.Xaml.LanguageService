using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlElement : XamlNode
{
    public TextSpan OpenNameSpan { get; }
    public TextSpan? CloseNameSpan { get; internal set; }
    // Full extent of the open tag (`<name …>` / `<name …/>`), both angle
    // brackets included. Null when the tag never reached its '>' (EOF or
    // recovery cut): the open tag then runs to the element's end, so every
    // position inside the element is still tag interior.
    public TextSpan? OpenTagSpan { get; internal set; }
    public bool IsSelfClosing { get; internal set; }

    public IList<XamlAttribute> Attributes { get; } = [];
    public IList<XamlNode> Children { get; } = [];

    public XamlElement(TextSpan openNameSpan) : base(default)
    {
        OpenNameSpan = openNameSpan;
    }

    public override XamlNode FindNodeAt(int line, int column)
    {
        if (Attributes is IReadOnlyList<XamlAttribute> attrs && attrs.Count > 0)
        {
            var i = SpanSearch.FindContaining(attrs, line, column, static a => a.Span);
            if (i >= 0)
                return attrs[i].FindNodeAt(line, column);
        }
        if (Children is IReadOnlyList<XamlNode> children && children.Count > 0)
        {
            var i = SpanSearch.FindContaining(children, line, column, static c => c.Span);
            if (i >= 0)
                return children[i].FindNodeAt(line, column);
        }
        return this;
    }
}
