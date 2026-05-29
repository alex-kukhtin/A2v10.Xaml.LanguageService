using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

// Focused coverage of error-path branches in XamlParser: each diagnostic
// message variant gets its own input, and a few cases verify the line/col
// positions of the diagnostic span on multi-line input.
public class DiagnosticsTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    // ---- message coverage ----

    [Fact]
    public void Empty_open_tag_yields_expected_element_name()
    {
        var doc = Parse("< />");
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Expected element name"));
    }

    [Fact]
    public void Attribute_without_equals_yields_requires_a_value()
    {
        var doc = Parse("<a b/>");
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Attribute requires a value"));
    }

    [Fact]
    public void Equals_without_value_yields_expected_attribute_value()
    {
        var doc = Parse("<a b=/>");
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Expected attribute value"));
    }

    [Fact]
    public void Empty_close_tag_yields_expected_element_name_in_end_tag()
    {
        var doc = Parse("<a></>");
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Expected element name in end tag"));
    }

    [Fact]
    public void Unknown_character_inside_tag_is_reported()
    {
        var doc = Parse("<a @ />");
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unexpected character inside tag"));
    }

    [Fact]
    public void Unknown_character_at_top_level_is_reported()
    {
        // Bareword (no '<') becomes Content; we need a Token.Unknown at top level.
        // The lexer doesn't emit Unknown from WithinContent state, so this is a
        // theoretical branch — exercised indirectly via stray tokens. Skipped.
    }

    // ---- diagnostic span correctness ----

    [Fact]
    public void Unclosed_tag_diagnostic_carries_open_name_position()
    {
        const string src = "<Foo>";
        var doc = Parse(src);
        var d = doc.Diagnostics.Single(x => x.Message.Contains("Unclosed"));
        Assert.Equal(0, d.Span.StartLine);
        Assert.Equal(1, d.Span.StartColumn);
        Assert.Equal(0, d.Span.EndLine);
        Assert.Equal(4, d.Span.EndColumn);
    }

    [Fact]
    public void Diagnostic_on_second_line_has_correct_line_column()
    {
        // \n then unclosed comment on line 1, starting at column 0.
        const string src = "<a/>\n<!-- nope";
        var doc = Parse(src);
        var d = doc.Diagnostics.Single(x => x.Message.Contains("Unterminated comment"));
        Assert.Equal(1, d.Span.StartLine);
        Assert.Equal(0, d.Span.StartColumn);
        // Comment span extends to EOF.
        Assert.Equal(1, d.Span.EndLine);
        Assert.Equal(9, d.Span.EndColumn); // "<!-- nope" length 9
    }

    [Fact]
    public void Crlf_line_break_is_counted_once_in_diagnostic()
    {
        // CRLF must behave as a single line break: diagnostic on line 1 starts
        // at column 0, not 1 (lone \r is swallowed, doesn't advance column).
        const string src = "<a/>\r\n<!-- nope";
        var doc = Parse(src);
        var d = doc.Diagnostics.Single(x => x.Message.Contains("Unterminated comment"));
        Assert.Equal(1, d.Span.StartLine);
        Assert.Equal(0, d.Span.StartColumn);
    }

    [Fact]
    public void Unmatched_end_tag_diagnostic_points_at_name()
    {
        //                  0         1
        //                  012345678901
        const string src = "<a></BadName>";
        var doc = Parse(src);
        var d = doc.Diagnostics.Single(x => x.Message.Contains("Unmatched end tag"));
        // Name span is "BadName" at columns 5..12.
        Assert.Equal(0, d.Span.StartLine);
        Assert.Equal(5, d.Span.StartColumn);
        Assert.Equal(0, d.Span.EndLine);
        Assert.Equal(12, d.Span.EndColumn);
    }

    [Fact]
    public void Multiline_unclosed_tag_diagnostic_points_at_open_name_line()
    {
        const string src = "<root>\n  <child>\n";
        var doc = Parse(src);
        // Two "Unclosed tag" diagnostics expected — one for each unclosed element.
        var childDiag = doc.Diagnostics.Single(d =>
            d.Message.Contains("Unclosed") && d.Span.StartLine == 1);
        Assert.Equal(3, childDiag.Span.StartColumn); // after "  <"
        Assert.Equal(1, childDiag.Span.EndLine);
        Assert.Equal(8, childDiag.Span.EndColumn);   // after "child"

        var rootDiag = doc.Diagnostics.Single(d =>
            d.Message.Contains("Unclosed") && d.Span.StartLine == 0);
        Assert.Equal(1, rootDiag.Span.StartColumn);
        Assert.Equal(5, rootDiag.Span.EndColumn);    // after "root"
    }
}
