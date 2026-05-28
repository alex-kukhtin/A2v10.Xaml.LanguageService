using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlElement : XamlNode
{
    public TextSpan OpenNameSpan { get; }
    public TextSpan? CloseNameSpan { get; internal set; }
    public bool IsSelfClosing { get; internal set; }

    public IList<XamlAttribute> Attributes { get; } = new List<XamlAttribute>();
    public IList<XamlNode> Children { get; } = new List<XamlNode>();

    public XamlElement(TextSpan openNameSpan) : base(default)
    {
        OpenNameSpan = openNameSpan;
    }

    public override XamlNode FindNodeAt(int offset)
    {
        if (Attributes is IReadOnlyList<XamlAttribute> attrs && attrs.Count > 0)
        {
            var i = SpanSearch.FindContaining(attrs, offset, static a => a.Span);
            if (i >= 0)
                return attrs[i].FindNodeAt(offset);
        }
        if (Children is IReadOnlyList<XamlNode> children && children.Count > 0)
        {
            var i = SpanSearch.FindContaining(children, offset, static c => c.Span);
            if (i >= 0)
                return children[i].FindNodeAt(offset);
        }
        return this;
    }
}
