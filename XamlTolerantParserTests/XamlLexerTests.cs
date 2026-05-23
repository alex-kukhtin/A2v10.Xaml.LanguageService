using Xunit;
using XamlTolerantParser;
using static XamlTolerantParserTests.LexerTestHelpers;

namespace XamlTolerantParserTests;

public class XamlLexerTests
{
    [Fact]
    public void Empty_input_yields_only_eof()
    {
        var tokens = TokenizeAll("");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
        Assert.Equal(new TextSpan(0, 0), tokens[0].Span);
    }

    [Fact]
    public void Plain_text_is_single_content_token()
    {
        const string src = "hello world";
        var tokens = TokenizeAll(src);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Content, tokens[0].Kind);
        Assert.Equal(src, tokens[0].TextOf(src));
        Assert.Equal(TokenKind.EndOfFile, tokens[1].Kind);
    }

    [Fact]
    public void Self_closing_empty_tag()
    {
        const string src = "<Button/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenKind.StartTagOpen, tokens[0].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal("Button", tokens[1].TextOf(src));
        Assert.Equal(TokenKind.SelfTagClose, tokens[2].Kind);
        Assert.Equal(TokenKind.EndOfFile, tokens[3].Kind);
    }

    [Fact]
    public void Open_and_close_tag_pair()
    {
        const string src = "<Button></Button>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.StartTagOpen, tokens[0].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal("Button", tokens[1].TextOf(src));
        Assert.Equal(TokenKind.TagClose, tokens[2].Kind);
        Assert.Equal(TokenKind.EndTagOpen, tokens[3].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[4].Kind);
        Assert.Equal("Button", tokens[4].TextOf(src));
        Assert.Equal(TokenKind.TagClose, tokens[5].Kind);
        Assert.Equal(TokenKind.EndOfFile, tokens[6].Kind);
    }

    [Fact]
    public void Attribute_with_double_quoted_value()
    {
        const string src = "<Button Width=\"100\"/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.StartTagOpen, tokens[0].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal(TokenKind.AttributeName, tokens[2].Kind);
        Assert.Equal("Width", tokens[2].TextOf(src));
        Assert.Equal(TokenKind.Equals, tokens[3].Kind);
        Assert.Equal(TokenKind.AttributeValue, tokens[4].Kind);
        Assert.Equal("\"100\"", tokens[4].TextOf(src));
        Assert.False(tokens[4].IsUnterminated);
        Assert.Equal(TokenKind.SelfTagClose, tokens[5].Kind);
    }

    [Fact]
    public void Attribute_with_single_quoted_value()
    {
        const string src = "<a x='1'/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.AttributeValue, tokens[4].Kind);
        Assert.Equal("'1'", tokens[4].TextOf(src));
    }

    [Fact]
    public void Multiple_attributes()
    {
        const string src = "<Button Width=\"100\" Height=\"50\"/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.AttributeName, tokens[2].Kind);
        Assert.Equal("Width", tokens[2].TextOf(src));
        Assert.Equal(TokenKind.Equals, tokens[3].Kind);
        Assert.Equal(TokenKind.AttributeValue, tokens[4].Kind);
        Assert.Equal(TokenKind.AttributeName, tokens[5].Kind);
        Assert.Equal("Height", tokens[5].TextOf(src));
        Assert.Equal(TokenKind.Equals, tokens[6].Kind);
        Assert.Equal(TokenKind.AttributeValue, tokens[7].Kind);
    }

    [Fact]
    public void Element_name_with_colon_prefix()
    {
        const string src = "<x:Button/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal("x:Button", tokens[1].TextOf(src));
    }

    [Fact]
    public void Property_element_name_with_dot()
    {
        const string src = "<Button.Background/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal("Button.Background", tokens[1].TextOf(src));
    }

    [Fact]
    public void Attribute_name_with_colon_prefix()
    {
        const string src = "<Button x:Name=\"b\"/>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.AttributeName, tokens[2].Kind);
        Assert.Equal("x:Name", tokens[2].TextOf(src));
    }

    [Fact]
    public void Text_between_tags()
    {
        const string src = "<a>hello</a>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.StartTagOpen, tokens[0].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal(TokenKind.TagClose, tokens[2].Kind);
        Assert.Equal(TokenKind.Content, tokens[3].Kind);
        Assert.Equal("hello", tokens[3].TextOf(src));
        Assert.Equal(TokenKind.EndTagOpen, tokens[4].Kind);
    }

    [Fact]
    public void Comment_well_formed()
    {
        const string src = "<!-- hi -->";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.Comment, tokens[0].Kind);
        Assert.Equal(src, tokens[0].TextOf(src));
        Assert.False(tokens[0].IsUnterminated);
    }

    [Fact]
    public void Comment_unterminated_consumes_to_eof_with_flag()
    {
        const string src = "<!-- never closed";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.Comment, tokens[0].Kind);
        Assert.True(tokens[0].IsUnterminated);
        Assert.Equal(src, tokens[0].TextOf(src));
    }

    [Fact]
    public void CData_well_formed()
    {
        const string src = "<![CDATA[ x < y ]]>";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.CData, tokens[0].Kind);
        Assert.Equal(src, tokens[0].TextOf(src));
        Assert.False(tokens[0].IsUnterminated);
    }

    [Fact]
    public void CData_unterminated_consumes_to_eof_with_flag()
    {
        const string src = "<![CDATA[ never closed";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.CData, tokens[0].Kind);
        Assert.True(tokens[0].IsUnterminated);
    }

    [Fact]
    public void Unterminated_attribute_value_consumes_to_eof_with_flag()
    {
        const string src = "<a x=\"abc";
        var tokens = TokenizeAll(src);
        var attrValue = System.Array.Find(tokens.ToArray(), t => t.Kind == TokenKind.AttributeValue);
        Assert.True(attrValue.IsUnterminated);
        Assert.Equal("\"abc", attrValue.TextOf(src));
    }

    [Fact]
    public void Whitespace_inside_tag_is_skipped()
    {
        const string src = "<a   x=\"1\"   />";
        var tokens = TokenizeAll(src);
        Assert.Equal(TokenKind.StartTagOpen, tokens[0].Kind);
        Assert.Equal(TokenKind.ElementName, tokens[1].Kind);
        Assert.Equal(TokenKind.AttributeName, tokens[2].Kind);
        Assert.Equal(TokenKind.Equals, tokens[3].Kind);
        Assert.Equal(TokenKind.AttributeValue, tokens[4].Kind);
        Assert.Equal(TokenKind.SelfTagClose, tokens[5].Kind);
    }

    [Fact]
    public void Back_returns_last_token_on_next_call()
    {
        var lex = new XamlLexer("<a/>");
        var t1 = lex.NextToken();
        lex.Back();
        var t1again = lex.NextToken();
        Assert.Equal(t1, t1again);
        var t2 = lex.NextToken();
        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void Back_twice_throws()
    {
        var lex = new XamlLexer("<a/>");
        lex.NextToken();
        lex.Back();
        Assert.Throws<System.InvalidOperationException>(() => lex.Back());
    }

    [Fact]
    public void Eof_is_idempotent()
    {
        var lex = new XamlLexer("");
        Assert.Equal(TokenKind.EndOfFile, lex.NextToken().Kind);
        Assert.Equal(TokenKind.EndOfFile, lex.NextToken().Kind);
        Assert.Equal(TokenKind.EndOfFile, lex.NextToken().Kind);
    }

    [Fact]
    public void Unterminated_attribute_value_with_newline_keeps_going()
    {
        // No heuristic for '\n' yet — the value extends past newline up to next quote/EOF.
        const string src = "<a x=\"line1\nline2\"/>";
        var tokens = TokenizeAll(src);
        var attrValue = System.Array.Find(tokens.ToArray(), t => t.Kind == TokenKind.AttributeValue);
        Assert.False(attrValue.IsUnterminated);
        Assert.Equal("\"line1\nline2\"", attrValue.TextOf(src));
    }
}
