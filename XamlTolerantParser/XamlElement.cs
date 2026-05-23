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
}
