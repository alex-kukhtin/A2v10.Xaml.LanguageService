using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class ExtensionContextAtTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);
    private static XamlPositionContext At(XamlDocument doc, int column) => doc.ContextAt(0, column);

    // --- type name ---------------------------------------------------------

    [Fact]
    public void Inside_extension_type_name_is_TypeName()
    {
        //                  0         1         2
        //                  0123456789012345678901234
        const string src = "<X Tag=\"{Binding}\"/>";
        var doc = Parse(src);
        // Column 11 — middle of "Binding".
        var ctx = Assert.IsType<ExtensionTypeNameContext>(At(doc, 11));
        Assert.Equal("Binding", src.Substring(ctx.Extension.TypeNameSpan.Start, ctx.Extension.TypeNameSpan.Length));
    }

    [Fact]
    public void Right_after_open_brace_with_no_type_is_TypeName()
    {
        // `{` only — empty type name. Column 9 is right after `{`.
        const string src = "<X Tag=\"{\"/>";
        var doc = Parse(src);
        var ctx = Assert.IsType<ExtensionTypeNameContext>(At(doc, 9));
        Assert.Equal(0, ctx.Extension.TypeNameSpan.Length);
    }

    [Fact]
    public void Right_after_type_name_end_is_TypeName()
    {
        const string src = "<X Tag=\"{Binding}\"/>";
        var doc = Parse(src);
        // Column 16 — just after 'g' of "Binding".
        Assert.IsType<ExtensionTypeNameContext>(At(doc, 16));
    }

    // --- interior ----------------------------------------------------------

    [Fact]
    public void Whitespace_between_args_is_Interior()
    {
        //                  0         1         2         3
        //                  012345678901234567890123456789012345678
        const string src = "<X Tag=\"{Binding Path=N , Mode=T}\"/>";
        var doc = Parse(src);
        // Column 25 — the space right after ','. Not inside any arg span,
        // not inside the type name, not at the `=` probe trigger.
        Assert.IsType<ExtensionInteriorContext>(At(doc, 25));
    }

    [Fact]
    public void Just_before_close_brace_is_Interior()
    {
        const string src = "<X Tag=\"{Binding }\"/>";
        var doc = Parse(src);
        // Column 17 — just before '}'.
        Assert.IsType<ExtensionInteriorContext>(At(doc, 17));
    }

    // --- named argument name ----------------------------------------------

    [Fact]
    public void Inside_named_arg_name_is_ArgName()
    {
        const string src = "<X Tag=\"{Binding Path=Name}\"/>";
        var doc = Parse(src);
        // Column 19 — middle of "Path".
        var ctx = Assert.IsType<ExtensionArgNameContext>(At(doc, 19));
        Assert.Equal("Path", src.Substring(ctx.Argument.NameSpan.Start, ctx.Argument.NameSpan.Length));
    }

    // --- named argument value ---------------------------------------------

    [Fact]
    public void Inside_named_arg_value_is_ArgValue_with_string_value()
    {
        const string src = "<X Tag=\"{Binding Path=Name}\"/>";
        var doc = Parse(src);
        // Column 24 — middle of "Name".
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, 24));
        var sv = Assert.IsType<XamlExtensionStringValue>(ctx.Value);
        Assert.Equal("Name", src.Substring(sv.Span.Start, sv.Span.Length));
        Assert.IsType<XamlNamedArgument>(ctx.Argument);
    }

    [Fact]
    public void Right_after_equals_with_no_value_is_ArgValue_null()
    {
        const string src = "<X Tag=\"{Binding Path=}\"/>";
        var doc = Parse(src);
        // Column 22 — right after '='.
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, 22));
        Assert.Null(ctx.Value);
        Assert.IsType<XamlNamedArgument>(ctx.Argument);
    }

    // --- positional argument ----------------------------------------------

    [Fact]
    public void Inside_positional_value_is_ArgValue_positional()
    {
        const string src = "<X Tag=\"{Binding Name}\"/>";
        var doc = Parse(src);
        // Column 19 — middle of "Name".
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, 19));
        Assert.IsType<XamlPositionalArgument>(ctx.Argument);
        Assert.IsType<XamlExtensionStringValue>(ctx.Value);
    }

    // --- nested ------------------------------------------------------------

    [Fact]
    public void Type_name_of_nested_extension()
    {
        //                  0         1         2         3
        //                  012345678901234567890123456789012345
        const string src = "<X Tag=\"{Binding Source={StaticResource Foo}}\"/>";
        var doc = Parse(src);
        // Find column of 'S' in "StaticResource".
        var col = src.IndexOf("StaticResource") + 5; // middle
        var ctx = Assert.IsType<ExtensionTypeNameContext>(At(doc, col));
        Assert.Equal("StaticResource",
            src.Substring(ctx.Extension.TypeNameSpan.Start, ctx.Extension.TypeNameSpan.Length));
    }

    [Fact]
    public void Nested_arg_value_is_classified_against_inner_extension()
    {
        const string src = "<X Tag=\"{Binding Source={StaticResource Foo}}\"/>";
        var doc = Parse(src);
        var col = src.IndexOf("Foo") + 1;
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, col));
        // The inner extension is the immediately-enclosing one.
        Assert.Equal("StaticResource",
            src.Substring(ctx.Extension.TypeNameSpan.Start, ctx.Extension.TypeNameSpan.Length));
        Assert.IsType<XamlPositionalArgument>(ctx.Argument);
    }

    // --- owner wiring ------------------------------------------------------

    [Fact]
    public void Element_and_attribute_are_owners_of_extension()
    {
        const string src = "<Button Tag=\"{Binding Path=Name}\"/>";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var attr = elem.Attributes[0];

        var col = src.IndexOf("Path") + 1;
        var ctx = Assert.IsType<ExtensionArgNameContext>(At(doc, col));
        Assert.Same(elem, ctx.Element);
        Assert.Same(attr, ctx.Attribute);
    }

    [Fact]
    public void Owners_resolved_through_nested_extension()
    {
        const string src = "<Button Tag=\"{Binding Source={StaticResource Foo}}\"/>";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var attr = elem.Attributes[0];

        var col = src.IndexOf("Foo") + 1;
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, col));
        Assert.Same(elem, ctx.Element);
        Assert.Same(attr, ctx.Attribute);
    }

    // --- unterminated ------------------------------------------------------

    [Fact]
    public void Inside_unterminated_extension_still_classifies()
    {
        const string src = "<X Tag=\"{Binding Path=Na";
        var doc = Parse(src);
        var col = src.IndexOf("Na") + 1;
        var ctx = Assert.IsType<ExtensionArgValueContext>(At(doc, col));
        Assert.False(ctx.Extension.IsClosed);
    }

    // --- regression: plain value still classifies as AttributeValue --------

    [Fact]
    public void Plain_attribute_value_still_AttributeValue()
    {
        const string src = "<X Tag=\"hello\"/>";
        var doc = Parse(src);
        Assert.IsType<AttributeValueContext>(At(doc, 10));
    }
}
