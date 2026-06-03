// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Linq;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using XamlLanguageServer;
using XamlTolerantParser;

using Xunit;

namespace XamlLanguageServerTests;

// Tests the pure node -> token-type mapping (SemanticTokensHandler.Tokens), independent
// of the LSP builder.
//
// These assert the mapping's STRUCTURE — which entities share a colour, the positional-
// vs-named-argument distinction, nested extensions not being flattened — NOT the literal
// SemanticTokenType each role is painted with. The concrete palette (Element=String,
// Name=Type, Value=Keyword today) is a cosmetic choice that gets retuned by eye; pinning
// the literal type would force a test rewrite on every tweak. Instead each role's colour
// is DISCOVERED from a minimal sample, then everything else is compared against it.
public class SemanticTokensTests
{
    private static (string Text, SemanticTokenType Type)[] Tokens(string src)
    {
        var doc = XamlParser.Parse(src);
        return [.. SemanticTokensHandler.Tokens(doc).Select(t => (doc.TextOf(t.Span), t.Type))];
    }

    private static SemanticTokenType TypeOf(string src, string text) =>
        Tokens(src).First(t => t.Text == text).Type;

    // The three palette roles, discovered (not hardcoded) from canonical samples:
    private static readonly SemanticTokenType ElementRole = TypeOf("<Root/>", "Root");
    private static readonly SemanticTokenType NameRole = TypeOf("<a b=\"1\"/>", "b");
    private static readonly SemanticTokenType ValueRole = TypeOf("<a b=\"1\"/>", "1");

    [Fact]
    public void The_three_roles_are_visually_distinct()
    {
        Assert.NotEqual(ElementRole, NameRole);
        Assert.NotEqual(ElementRole, ValueRole);
        Assert.NotEqual(NameRole, ValueRole);
    }

    [Fact]
    public void Open_and_close_tag_names_share_the_element_role()
    {
        var tokens = Tokens("<Button></Button>");
        Assert.Equal(2, tokens.Length);
        Assert.All(tokens, t => Assert.Equal("Button", t.Text));
        Assert.All(tokens, t => Assert.Equal(ElementRole, t.Type));
    }

    [Fact]
    public void Self_closing_tag_has_only_the_open_name()
    {
        var tokens = Tokens("<Button/>");
        Assert.Single(tokens);
        Assert.Equal(("Button", ElementRole), tokens[0]);
    }

    [Fact]
    public void Attribute_name_is_name_role_and_value_is_value_role()
    {
        var tokens = Tokens("<a Width=\"100\"/>");
        Assert.Contains(("a", ElementRole), tokens);
        Assert.Contains(("Width", NameRole), tokens);
        Assert.Contains(("100", ValueRole), tokens);
    }

    [Fact]
    public void Comment_has_its_own_colour()
    {
        // Comments are coloured distinctly from all three structural roles.
        var commentType = Tokens("<a><!-- hi --></a>").Single(t => t.Text.Contains("hi")).Type;
        Assert.NotEqual(ElementRole, commentType);
        Assert.NotEqual(NameRole, commentType);
        Assert.NotEqual(ValueRole, commentType);
    }

    [Fact]
    public void Cdata_shares_the_comment_role()
    {
        // CDATA is coloured like a comment — both read as inert text.
        var commentType = Tokens("<a><!-- hi --></a>").Single(t => t.Text.Contains("hi")).Type;
        var cdataType = Tokens("<a><![CDATA[raw]]></a>").Single(t => t.Text.Contains("raw")).Type;
        Assert.Equal(commentType, cdataType);
    }

    [Fact]
    public void Plain_text_content_is_not_coloured()
    {
        // XamlText (markup text content) is intentionally left default-coloured.
        var tokens = Tokens("<a>hello</a>");
        Assert.DoesNotContain(tokens, t => t.Text == "hello");
    }

    [Fact]
    public void Markup_extension_groups_with_the_element_name_and_value_roles()
    {
        var tokens = Tokens("<a b=\"{Bind Path=X}\"/>");
        Assert.Contains(("Bind", ElementRole), tokens);   // extension type ~ element
        Assert.Contains(("Path", NameRole), tokens);      // named-arg name ~ property name
        Assert.Contains(("X", ValueRole), tokens);        // named-arg value ~ value
        // The extension-hosting value is NOT also coloured as one big token.
        Assert.DoesNotContain(tokens, t => t.Text == "{Bind Path=X}");
    }

    [Fact]
    public void Positional_argument_groups_with_names_not_values()
    {
        // The key in {StaticResource Foo} is a positional argument — it shares the NAME
        // role, deliberately NOT the value role (the key invariant of the parent-based
        // colouring that distinguishes positional from named-argument values).
        var tokens = Tokens("<a b=\"{StaticResource Foo}\"/>");
        Assert.Contains(("StaticResource", ElementRole), tokens);
        Assert.Contains(("Foo", NameRole), tokens);
    }

    [Fact]
    public void Nested_extension_is_coloured_as_an_extension_not_a_flat_value()
    {
        // {Bind Source={Static Foo}} — the inner extension is coloured by its own nodes
        // (Static ~ element, Foo ~ positional name), never collapsed into one value token.
        var tokens = Tokens("<a b=\"{Bind Source={Static Foo}}\"/>");
        Assert.Contains(("Bind", ElementRole), tokens);
        Assert.Contains(("Static", ElementRole), tokens);
        Assert.Contains(("Source", NameRole), tokens);
        Assert.Contains(("Foo", NameRole), tokens);
    }

    [Fact]
    public void Tokens_are_sorted_by_position()
    {
        // Close tag name comes last in source though Visit emits it during the
        // element's pre-children visit — collect-then-sort must reorder it.
        var doc = XamlParser.Parse("<a x=\"1\"><b/></a>");
        var spans = SemanticTokensHandler.Tokens(doc).Select(t => t.Span).ToArray();
        for (var i = 1; i < spans.Length; i++)
        {
            var prev = spans[i - 1];
            var cur = spans[i];
            var ordered = cur.StartLine > prev.StartLine
                || (cur.StartLine == prev.StartLine && cur.StartColumn >= prev.StartColumn);
            Assert.True(ordered, $"token {i} out of order");
        }
        // Sanity: the closing 'a' is the last token.
        var last = spans[^1];
        Assert.Equal("a", doc.TextOf(last));
    }
}
