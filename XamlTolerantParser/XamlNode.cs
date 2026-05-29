namespace XamlTolerantParser;

public abstract class XamlNode
{
    public TextSpan Span { get; internal set; }
    public XamlNode? Parent { get; internal set; }

    protected XamlNode(TextSpan span)
    {
        Span = span;
    }

    // Returns the most specific node whose span contains (line, column).
    // Precondition: this.Span.Contains(line, column) — the caller has already narrowed.
    // Leaves return themselves; composite nodes (element, attribute) override to descend.
    public virtual XamlNode FindNodeAt(int line, int column) => this;

    // Closest enclosing element walking strictly upwards (never returns `this`).
    // Useful when content rules (e.g. [ContentProperty] collection membership)
    // depend on which tag the current node sits inside, regardless of whether
    // the current node is an attribute, value, text or another element.
    public XamlElement? OwnerElement
    {
        get
        {
            var p = Parent;
            while (p != null)
            {
                if (p is XamlElement e)
                    return e;
                p = p.Parent;
            }
            return null;
        }
    }
}
