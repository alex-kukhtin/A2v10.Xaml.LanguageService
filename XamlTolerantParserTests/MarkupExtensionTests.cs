using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class MarkupExtensionTests
{
    static XamlDocument Parse(string src) => XamlParser.Parse(src);

    static string TextOf(string src, TextSpan span) =>
        src.Substring(span.Start, span.Length);

    // Pull the XamlExtension from the first attribute of the first root.
    static XamlExtension ExtractFirst(string src)
    {
        var doc = Parse(src);
        var elem = Assert.IsType<XamlElement>(doc.Roots[0]);
        var attr = elem.Attributes[0];
        var value = attr.Value!;
        Assert.NotNull(value.Extension);
        return value.Extension!;
    }

    // --- detection & escape ---------------------------------------------

    [Fact]
    public void Plain_string_value_has_no_extension()
    {
        const string src = "<Button Width=\"42\" />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        Assert.Null(elem.Attributes[0].Value!.Extension);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Empty_value_has_no_extension()
    {
        const string src = "<Button Width=\"\" />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        Assert.Null(elem.Attributes[0].Value!.Extension);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Escape_brace_sequence_is_not_an_extension()
    {
        // {} at start of value content is the XAML literal-string escape.
        const string src = "<Button Text=\"{}{0:N2}\" />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        Assert.Null(elem.Attributes[0].Value!.Extension);
        Assert.Empty(doc.Diagnostics);
    }

    // --- bare type ------------------------------------------------------

    [Fact]
    public void Bare_type_only()
    {
        const string src = "<Button Width=\"{Binding}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal("Binding", TextOf(src, ext.TypeNameSpan));
        Assert.Empty(ext.Arguments);
        Assert.True(ext.IsClosed);
        Assert.Empty(Parse(src).Diagnostics);
    }

    [Fact]
    public void Bare_type_with_prefix()
    {
        const string src = "<Button Tag=\"{x:Static local:Foo.Bar}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal("x:Static", TextOf(src, ext.TypeNameSpan));
        var arg = Assert.Single(ext.Arguments);
        var pos = Assert.IsType<XamlPositionalArgument>(arg);
        var sv = Assert.IsType<XamlExtensionStringValue>(pos.Value);
        Assert.Equal("local:Foo.Bar", TextOf(src, sv.Span));
    }

    [Fact]
    public void Type_with_leading_inner_whitespace()
    {
        const string src = "<Button Tag=\"{ Binding }\" />";
        var ext = ExtractFirst(src);
        Assert.Equal("Binding", TextOf(src, ext.TypeNameSpan));
        Assert.Empty(ext.Arguments);
        Assert.True(ext.IsClosed);
    }

    // --- positional & named arguments -----------------------------------

    [Fact]
    public void Single_positional_argument()
    {
        const string src = "<TextBlock Text=\"{Binding Name}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal("Binding", TextOf(src, ext.TypeNameSpan));
        var arg = Assert.Single(ext.Arguments);
        var pos = Assert.IsType<XamlPositionalArgument>(arg);
        var sv = Assert.IsType<XamlExtensionStringValue>(pos.Value);
        Assert.Equal("Name", TextOf(src, sv.Span));
    }

    [Fact]
    public void Single_named_argument()
    {
        const string src = "<TextBlock Text=\"{Binding Path=Name}\" />";
        var ext = ExtractFirst(src);
        var arg = Assert.Single(ext.Arguments);
        var named = Assert.IsType<XamlNamedArgument>(arg);
        Assert.Equal("Path", TextOf(src, named.NameSpan));
        var sv = Assert.IsType<XamlExtensionStringValue>(named.Value);
        Assert.Equal("Name", TextOf(src, sv.Span));
    }

    [Fact]
    public void Mixed_positional_and_named_arguments()
    {
        const string src = "<TextBlock Text=\"{Binding Name, Mode=TwoWay, FallbackValue=none}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal(3, ext.Arguments.Count);

        var pos = Assert.IsType<XamlPositionalArgument>(ext.Arguments[0]);
        Assert.Equal("Name", TextOf(src, ((XamlExtensionStringValue)pos.Value!).Span));

        var a1 = Assert.IsType<XamlNamedArgument>(ext.Arguments[1]);
        Assert.Equal("Mode", TextOf(src, a1.NameSpan));
        Assert.Equal("TwoWay", TextOf(src, ((XamlExtensionStringValue)a1.Value!).Span));

        var a2 = Assert.IsType<XamlNamedArgument>(ext.Arguments[2]);
        Assert.Equal("FallbackValue", TextOf(src, a2.NameSpan));
        Assert.Equal("none", TextOf(src, ((XamlExtensionStringValue)a2.Value!).Span));
    }

    [Fact]
    public void Whitespace_around_separators_is_tolerated()
    {
        const string src = "<TextBlock Text=\"{Binding  Path = Name ,  Mode = TwoWay }\" />";
        var ext = ExtractFirst(src);
        Assert.Equal(2, ext.Arguments.Count);
        var a0 = Assert.IsType<XamlNamedArgument>(ext.Arguments[0]);
        Assert.Equal("Path", TextOf(src, a0.NameSpan));
        Assert.Equal("Name", TextOf(src, ((XamlExtensionStringValue)a0.Value!).Span));
        var a1 = Assert.IsType<XamlNamedArgument>(ext.Arguments[1]);
        Assert.Equal("Mode", TextOf(src, a1.NameSpan));
        Assert.Equal("TwoWay", TextOf(src, ((XamlExtensionStringValue)a1.Value!).Span));
    }

    [Fact]
    public void Value_span_excludes_trailing_whitespace()
    {
        const string src = "<TextBlock Text=\"{Binding Name   }\" />";
        var ext = ExtractFirst(src);
        var pos = Assert.IsType<XamlPositionalArgument>(ext.Arguments[0]);
        var sv = Assert.IsType<XamlExtensionStringValue>(pos.Value);
        Assert.Equal("Name", TextOf(src, sv.Span));
    }

    [Fact]
    public void Path_with_dots_kept_as_single_value()
    {
        const string src = "<TextBlock Text=\"{Binding Source.Sub.Name}\" />";
        var ext = ExtractFirst(src);
        var pos = Assert.IsType<XamlPositionalArgument>(ext.Arguments[0]);
        var sv = Assert.IsType<XamlExtensionStringValue>(pos.Value);
        Assert.Equal("Source.Sub.Name", TextOf(src, sv.Span));
    }

    // --- nesting --------------------------------------------------------

    [Fact]
    public void Nested_extension_as_named_value()
    {
        const string src = "<TextBlock Text=\"{Binding Source={StaticResource Foo}}\" />";
        var ext = ExtractFirst(src);
        var named = Assert.IsType<XamlNamedArgument>(Assert.Single(ext.Arguments));
        Assert.Equal("Source", TextOf(src, named.NameSpan));
        var inner = Assert.IsType<XamlExtension>(named.Value);
        Assert.Equal("StaticResource", TextOf(src, inner.TypeNameSpan));
        var innerPos = Assert.IsType<XamlPositionalArgument>(Assert.Single(inner.Arguments));
        Assert.Equal("Foo", TextOf(src, ((XamlExtensionStringValue)innerPos.Value!).Span));
    }

    [Fact]
    public void Nested_extension_as_positional_value()
    {
        const string src = "<TextBlock Tag=\"{Foo {Bar Baz}}\" />";
        var ext = ExtractFirst(src);
        var pos = Assert.IsType<XamlPositionalArgument>(Assert.Single(ext.Arguments));
        var inner = Assert.IsType<XamlExtension>(pos.Value);
        Assert.Equal("Bar", TextOf(src, inner.TypeNameSpan));
    }

    // --- unterminated / recovery ---------------------------------------

    [Fact]
    public void Missing_close_brace_emits_diagnostic_but_keeps_args()
    {
        const string src = "<Button Tag=\"{Binding Name\" />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var ext = elem.Attributes[0].Value!.Extension!;
        Assert.NotNull(ext);
        Assert.False(ext.IsClosed);
        Assert.Single(ext.Arguments);
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unterminated markup extension"));
    }

    [Fact]
    public void Missing_type_name_emits_diagnostic()
    {
        const string src = "<Button Tag=\"{}\" />";
        // {} is the escape — this should be NOT an extension.
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        Assert.Null(elem.Attributes[0].Value!.Extension);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Brace_then_close_with_leading_ws_has_empty_type_name()
    {
        // '{ }' is NOT the {} escape (there's whitespace) — treated as an
        // extension with an empty type name. Diagnostic raised.
        const string src = "<Button Tag=\"{ }\" />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var ext = elem.Attributes[0].Value!.Extension;
        Assert.NotNull(ext);
        Assert.Equal(0, ext!.TypeNameSpan.Length);
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Expected markup extension type name"));
    }

    [Fact]
    public void Trailing_equals_with_no_value_keeps_arg_with_null_value()
    {
        const string src = "<Button Tag=\"{Binding Path=}\" />";
        var ext = ExtractFirst(src);
        var named = Assert.IsType<XamlNamedArgument>(Assert.Single(ext.Arguments));
        Assert.Equal("Path", TextOf(src, named.NameSpan));
        Assert.Null(named.Value);
    }

    [Fact]
    public void Trailing_comma_is_tolerated()
    {
        const string src = "<Button Tag=\"{Binding Name,}\" />";
        var ext = ExtractFirst(src);
        Assert.Single(ext.Arguments);
        Assert.True(ext.IsClosed);
    }

    [Fact]
    public void Double_comma_is_tolerated()
    {
        const string src = "<Button Tag=\"{Binding Name,,Mode=TwoWay}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal(2, ext.Arguments.Count);
    }

    [Fact]
    public void Unterminated_attribute_value_with_extension_still_parses()
    {
        // Closing quote missing — the lexer reports the attr value as
        // unterminated, but the mini-parser still works over whatever
        // ContentSpan it gets.
        const string src = "<Button Tag=\"{Binding Name";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var value = elem.Attributes[0].Value!;
        Assert.True(value.IsUnterminated);
        Assert.NotNull(value.Extension);
        Assert.False(value.Extension!.IsClosed);
    }

    [Fact]
    public void Unterminated_extension_with_closed_attr_value_stops_at_quote()
    {
        // Closing '"' exists, closing '}' is missing — the lexer ends
        // ContentSpan right before the '"', so the extension span ends there.
        const string src = "<Button Tag=\"{Binding Name\" />";
        var doc = Parse(src);
        var ext = ((XamlElement)doc.Roots[0]).Attributes[0].Value!.Extension!;
        // Span ends at the position of the closing '"' (exclusive end).
        var openQuote = src.IndexOf('"');
        var closeQuote = src.IndexOf('"', openQuote + 1);
        Assert.Equal(closeQuote, ext.Span.End);
        Assert.False(ext.IsClosed);
    }

    [Fact]
    public void Unterminated_extension_without_closing_quote_stops_at_tag_close()
    {
        // No closing '"' and no '}' — the lexer would otherwise pull
        // ContentSpan to EOF. The mini-parser must stop at '>' so the
        // extension span doesn't engulf the rest of the document.
        const string src = "<Button Tag=\"{Binding Name />";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        var ext = elem.Attributes[0].Value!.Extension!;
        Assert.False(ext.IsClosed);
        // Extension span must NOT include '>' (the tag close).
        var gtIdx = src.IndexOf('>');
        Assert.True(ext.Span.End <= gtIdx,
            $"Extension span end {ext.Span.End} should not pass '>' at {gtIdx}");
    }

    [Fact]
    public void Less_than_inside_extension_value_terminates_scan()
    {
        // Defensive: '<' starts a new tag and can't legally appear inside
        // an extension. Scan stops there instead of swallowing the markup.
        const string src = "<Button Tag=\"{Binding Name<Other/>\" />";
        var doc = Parse(src);
        var ext = ((XamlElement)doc.Roots[0]).Attributes[0].Value!.Extension!;
        Assert.False(ext.IsClosed);
        Assert.True(ext.Span.End <= src.IndexOf("<Other"));
    }

    // --- spans / FindNodeAt --------------------------------------------

    [Fact]
    public void Extension_span_covers_braces()
    {
        const string src = "<Button Width=\"{Binding Name}\" />";
        var ext = ExtractFirst(src);
        Assert.Equal("{Binding Name}", TextOf(src, ext.Span));
    }

    [Fact]
    public void FindNodeAt_descends_into_argument()
    {
        const string src = "<Button Width=\"{Binding Path=Name}\" />";
        var doc = Parse(src);
        // Source: <Button Width="{Binding Path=Name}" />
        // Position of 'N' in Name: after `Path=`
        var nIdx = src.IndexOf("Name");
        var node = doc.FindNodeAt(0, nIdx + 1);
        var sv = Assert.IsType<XamlExtensionStringValue>(node);
        Assert.Equal("Name", TextOf(src, sv.Span));
    }

    [Fact]
    public void FindNodeAt_lands_on_named_argument_at_name()
    {
        const string src = "<Button Width=\"{Binding Path=Name}\" />";
        var doc = Parse(src);
        var pIdx = src.IndexOf("Path");
        var node = doc.FindNodeAt(0, pIdx + 1);
        var arg = Assert.IsType<XamlNamedArgument>(node);
        Assert.Equal("Path", TextOf(src, arg.NameSpan));
    }

    [Fact]
    public void FindNodeAt_lands_on_extension_at_type_name()
    {
        const string src = "<Button Width=\"{Binding Path=Name}\" />";
        var doc = Parse(src);
        var bIdx = src.IndexOf("Binding");
        var node = doc.FindNodeAt(0, bIdx + 1);
        var ext = Assert.IsType<XamlExtension>(node);
        Assert.Equal("Binding", TextOf(src, ext.TypeNameSpan));
    }

    [Fact]
    public void Parent_chain_is_set_for_extension_tree()
    {
        const string src = "<Button Width=\"{Binding Source={StaticResource Foo}}\" />";
        var doc = Parse(src);
        var value = ((XamlElement)doc.Roots[0]).Attributes[0].Value!;
        var ext = value.Extension!;
        Assert.Same(value, ext.Parent);

        var outer = Assert.IsType<XamlNamedArgument>(ext.Arguments[0]);
        Assert.Same(ext, outer.Parent);

        var inner = Assert.IsType<XamlExtension>(outer.Value);
        Assert.Same(outer, inner.Parent);

        var innerArg = inner.Arguments[0];
        Assert.Same(inner, innerArg.Parent);
    }
}
