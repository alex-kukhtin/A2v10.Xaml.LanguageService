using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

// IdentifierSpanAt is the completion replace-range: the run of name chars around
// the caret, a pure text fact independent of the parse tree. These pin that it is
// uniform across positions and immune to the half-open-span exclusive-end quirk.
public class IdentifierSpanAtTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    [Fact]
    public void At_end_of_token_covers_the_whole_token()
    {
        //                  012345678
        const string src = "<Button>";
        var doc = Parse(src);
        // Caret after "Button" (col 7, the '>').
        var span = doc.IdentifierSpanAt(0, 7);
        Assert.Equal(1, span.Start);   // 'B'
        Assert.Equal(6, span.Length);  // "Button"
        Assert.Equal(1, span.StartColumn);
        Assert.Equal(7, span.EndColumn);
    }

    [Fact]
    public void In_the_middle_of_a_token_still_covers_the_whole_token()
    {
        const string src = "<Button>";
        var doc = Parse(src);
        // Caret inside "Button" (after "Bu", col 3) — back+forward walk still
        // yields the full token, not just the prefix.
        var span = doc.IdentifierSpanAt(0, 3);
        Assert.Equal(1, span.Start);
        Assert.Equal(6, span.Length); // "Button"
    }

    [Fact]
    public void Prefix_qualified_name_is_one_token_colon_included()
    {
        const string src = "<my:Grid>";
        var doc = Parse(src);
        // Caret after "my:Grid" (col 8). ':' is a name char, so the whole
        // qualified name is one replace range.
        var span = doc.IdentifierSpanAt(0, 8);
        Assert.Equal(1, span.Start);
        Assert.Equal(7, span.Length); // "my:Grid"
    }

    [Fact]
    public void Between_quotes_with_no_text_is_empty_at_caret()
    {
        //                  012345678
        const string src = "<a b=\"\"/>";
        var doc = Parse(src);
        // Caret between the two quotes (col 6). No name char touches it.
        var span = doc.IdentifierSpanAt(0, 6);
        Assert.Equal(0, span.Length);
        Assert.Equal(6, span.StartColumn);
    }

    [Fact]
    public void Inside_markup_extension_argument_covers_just_the_arg_token()
    {
        //                  0         1
        //                  012345678901234567
        const string src = "<X Tag=\"{Bind Dat}\"/>";
        var doc = Parse(src);
        // Caret right after "Dat" (col 17), at the '}'. The barrier '}' stops the
        // forward walk; the value quote is irrelevant. Covers exactly "Dat".
        var span = doc.IdentifierSpanAt(0, 17);
        Assert.Equal(14, span.Start);  // 'D'
        Assert.Equal(3, span.Length);  // "Dat"
        Assert.Equal(14, span.StartColumn);
        Assert.Equal(17, span.EndColumn);
    }
}
