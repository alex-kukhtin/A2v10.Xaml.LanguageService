using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class XamlParserTests
{
    static XamlDocument Parse(string src) => XamlParser.Parse(src);

    static string TextOf(string src, TextSpan span) =>
        src.Substring(span.Start, span.Length);

    static void AssertSpanContainsChildren(XamlNode node)
    {
        if (node is XamlElement e)
        {
            foreach (var a in e.Attributes)
            {
                Assert.True(e.Span.Start <= a.Span.Start && a.Span.End <= e.Span.End,
                    $"Attribute {a.Span} not within element {e.Span}");
                if (a.Value is { } v)
                {
                    Assert.True(a.Span.Start <= v.Span.Start && v.Span.End <= a.Span.End,
                        $"Value {v.Span} not within attribute {a.Span}");
                }
            }
            foreach (var c in e.Children)
            {
                Assert.True(e.Span.Start <= c.Span.Start && c.Span.End <= e.Span.End,
                    $"Child {c.Span} not within element {e.Span}");
                AssertSpanContainsChildren(c);
            }
        }
    }

    static void AssertAllSpansNested(XamlDocument doc)
    {
        foreach (var r in doc.Roots)
            AssertSpanContainsChildren(r);
    }

    [Fact]
    public void Empty_document_has_no_roots_no_diagnostics()
    {
        var doc = Parse("");
        Assert.Empty(doc.Roots);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Single_self_closing_element()
    {
        const string src = "<Button/>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        Assert.Single(doc.Roots);
        var elem = Assert.IsType<XamlElement>(doc.Roots[0]);
        Assert.True(elem.IsSelfClosing);
        Assert.Equal("Button", TextOf(src, elem.OpenNameSpan));
        Assert.Null(elem.CloseNameSpan);
        Assert.Equal(src, TextOf(src, elem.Span));
    }

    [Fact]
    public void Paired_element_records_open_and_close_spans()
    {
        const string src = "<Button></Button>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        var elem = Assert.IsType<XamlElement>(doc.Roots[0]);
        Assert.False(elem.IsSelfClosing);
        Assert.Equal("Button", TextOf(src, elem.OpenNameSpan));
        Assert.NotNull(elem.CloseNameSpan);
        Assert.Equal("Button", TextOf(src, elem.CloseNameSpan!.Value));
        Assert.Equal(src, TextOf(src, elem.Span));
    }

    [Fact]
    public void Attribute_with_value()
    {
        const string src = "<Button Width=\"100\"/>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        var elem = (XamlElement)doc.Roots[0];
        var attr = Assert.Single(elem.Attributes);
        Assert.Equal("Width", TextOf(src, attr.NameSpan));
        Assert.NotNull(attr.Value);
        Assert.Equal("\"100\"", TextOf(src, attr.Value!.Span));
        Assert.Equal("100", TextOf(src, attr.Value.ContentSpan));
        Assert.Equal('"', attr.Value.Quote);
        Assert.False(attr.Value.IsUnterminated);
    }

    [Fact]
    public void Multiple_attributes_in_order()
    {
        const string src = "<a x=\"1\" y='2'/>";
        var doc = Parse(src);
        var elem = (XamlElement)doc.Roots[0];
        Assert.Equal(2, elem.Attributes.Count);
        Assert.Equal("x", TextOf(src, elem.Attributes[0].NameSpan));
        Assert.Equal("y", TextOf(src, elem.Attributes[1].NameSpan));
        Assert.Equal('\'', elem.Attributes[1].Value!.Quote);
    }

    [Fact]
    public void Nested_elements_form_child_hierarchy()
    {
        const string src = "<a><b><c/></b></a>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        var a = (XamlElement)doc.Roots[0];
        var b = (XamlElement)Assert.Single(a.Children);
        var c = (XamlElement)Assert.Single(b.Children);
        Assert.Equal("c", TextOf(src, c.OpenNameSpan));
        Assert.True(c.IsSelfClosing);
    }

    [Fact]
    public void Text_between_tags_becomes_xaml_text_child()
    {
        const string src = "<a>hello</a>";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        var text = Assert.IsType<XamlText>(Assert.Single(a.Children));
        Assert.Equal("hello", TextOf(src, text.Span));
    }

    [Fact]
    public void Comment_inside_element()
    {
        const string src = "<a><!-- hi --></a>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        var a = (XamlElement)doc.Roots[0];
        var c = Assert.IsType<XamlComment>(Assert.Single(a.Children));
        Assert.Equal("<!-- hi -->", TextOf(src, c.Span));
    }

    [Fact]
    public void Cdata_inside_element()
    {
        const string src = "<a><![CDATA[x<y]]></a>";
        var doc = Parse(src);
        Assert.Empty(doc.Diagnostics);
        var a = (XamlElement)doc.Roots[0];
        var cd = Assert.IsType<XamlCData>(Assert.Single(a.Children));
        Assert.Equal("<![CDATA[x<y]]>", TextOf(src, cd.Span));
    }

    // ---- recovery / diagnostics ----

    [Fact]
    public void Unclosed_tag_at_eof_yields_diagnostic_and_span_to_eof()
    {
        const string src = "<a>";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        Assert.Single(doc.Diagnostics);
        Assert.Contains("Unclosed tag", doc.Diagnostics[0].Message);
        Assert.Equal(src.Length, a.Span.End);
    }

    [Fact]
    public void Mismatched_close_tag_is_ignored_with_diagnostic()
    {
        const string src = "<a></b>";
        var doc = Parse(src);
        Assert.Equal(2, doc.Diagnostics.Count); // unmatched </b>, unclosed <a>
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unmatched end tag"));
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unclosed tag"));
    }

    [Fact]
    public void Inner_unclosed_tag_closed_by_outer_close()
    {
        // <a><b></a>  — </a> matches up the stack; <b> gets force-closed with a diagnostic
        const string src = "<a><b></a>";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        var b = (XamlElement)Assert.Single(a.Children);
        Assert.Equal("b", TextOf(src, b.OpenNameSpan));
        Assert.Single(doc.Diagnostics);
        Assert.Contains("Unclosed tag", doc.Diagnostics[0].Message);
    }

    [Fact]
    public void Start_tag_without_close_recovers_on_next_start()
    {
        // <a <b/>  — first <a never sees '>'; recovery closes it as sibling-stop, then <b/> follows.
        const string src = "<a <b/>";
        var doc = Parse(src);
        Assert.Equal(2, doc.Roots.Count);
        var a = (XamlElement)doc.Roots[0];
        var b = (XamlElement)doc.Roots[1];
        Assert.Equal("a", TextOf(src, a.OpenNameSpan));
        Assert.Equal("b", TextOf(src, b.OpenNameSpan));
        Assert.True(b.IsSelfClosing);
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Tag is not closed"));
    }

    [Fact]
    public void Unterminated_attribute_value_produces_diagnostic_chain_but_keeps_value()
    {
        const string src = "<a x=\"abc";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        var attr = Assert.Single(a.Attributes);
        Assert.NotNull(attr.Value);
        Assert.True(attr.Value!.IsUnterminated);
        Assert.Equal("\"abc", TextOf(src, attr.Value.Span));
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unclosed tag"));
    }

    [Fact]
    public void Sibling_tag_below_broken_one_still_parses_cleanly()
    {
        // LemMinX-style recovery: a broken tag does not poison its siblings below.
        const string src = "<root>\n  <broken Property=\"v1 Other=\"v2\"/>\n  <good Width=\"10\"/>\n</root>";
        var doc = Parse(src);
        var root = (XamlElement)doc.Roots[0];
        var good = root.Children.OfType<XamlElement>().FirstOrDefault(e => TextOf(src, e.OpenNameSpan) == "good");
        Assert.NotNull(good);
        Assert.True(good!.IsSelfClosing);
        Assert.Equal("10", TextOf(src, good.Attributes.Single().Value!.ContentSpan));
    }

    [Fact]
    public void Unterminated_comment_produces_diagnostic()
    {
        const string src = "<!-- nope";
        var doc = Parse(src);
        Assert.Single(doc.Roots);
        Assert.IsType<XamlComment>(doc.Roots[0]);
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unterminated comment"));
    }

    [Theory]
    [InlineData("<a/>")]
    [InlineData("<a></a>")]
    [InlineData("<a><b/></a>")]
    [InlineData("<a><b><c/></b></a>")]
    [InlineData("<a x=\"1\" y='2'><b z=\"3\"/></a>")]
    [InlineData("<a>text<b/>more</a>")]
    [InlineData("<a><!-- comment --><b/></a>")]
    [InlineData("<a><![CDATA[stuff]]></a>")]
    // recovery cases
    [InlineData("<a>")]                           // unclosed
    [InlineData("<a></b>")]                       // mismatched
    [InlineData("<a><b></a>")]                    // inner unclosed
    [InlineData("<a <b/>")]                       // stray '<' mid-tag
    [InlineData("<a x=\"abc")]                    // unterminated attr value
    [InlineData("<a><b><c></a>")]                 // multi-level force-close
    [InlineData("<root><broken Property=\"v1 Other=\"v2\"/><good Width=\"10\"/></root>")]
    public void Span_of_every_node_contains_spans_of_its_children(string src)
    {
        var doc = Parse(src);
        AssertAllSpansNested(doc);
    }

    [Fact]
    public void Parent_is_set_on_children_attributes_and_values()
    {
        const string src = "<a x=\"1\"><b/>text</a>";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        Assert.Null(a.Parent);

        var attr = a.Attributes.Single();
        Assert.Same(a, attr.Parent);
        Assert.Same(attr, attr.Value!.Parent);

        var b = a.Children.OfType<XamlElement>().Single();
        Assert.Same(a, b.Parent);

        var text = a.Children.OfType<XamlText>().Single();
        Assert.Same(a, text.Parent);
    }

    [Fact]
    public void Unterminated_cdata_produces_diagnostic()
    {
        const string src = "<![CDATA[ nope";
        var doc = Parse(src);
        Assert.Single(doc.Roots);
        Assert.IsType<XamlCData>(doc.Roots[0]);
        Assert.Contains(doc.Diagnostics, d => d.Message.Contains("Unterminated CDATA"));
    }
}
