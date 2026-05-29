namespace XamlTolerantParser;

// Discriminator for "the right-hand side of an extension argument": either a
// nested XamlExtension or a raw XamlExtensionStringValue. Keeping the abstract
// base lets XamlExtensionArgument expose a single Value slot without unions.
public abstract class XamlExtensionValue : XamlNode
{
    protected XamlExtensionValue(TextSpan span) : base(span) { }
}
