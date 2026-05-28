using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class FindNodeAtTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    [Fact]
    public void Empty_document_returns_null()
    {
        var doc = Parse("");
        Assert.Null(doc.FindNodeAt(0));
    }

    [Fact]
    public void Leading_whitespace_yields_no_node()
    {
        // Whitespace-only character data is dropped by the parser, so an offset
        // before the first real root falls outside every node.
        const string src = "  <a/>";
        var doc = Parse(src);
        Assert.Null(doc.FindNodeAt(0));
        Assert.Null(doc.FindNodeAt(1));
    }

    [Fact]
    public void Offset_past_eof_returns_null()
    {
        const string src = "<a/>";
        var doc = Parse(src);
        Assert.Null(doc.FindNodeAt(src.Length));
    }

    [Fact]
    public void Offset_in_element_name_returns_element()
    {
        //                  0123456789
        const string src = "<Button/>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(3); // inside "Button"
        Assert.IsType<XamlElement>(node);
        Assert.Same(doc.Roots[0], node);
    }

    [Fact]
    public void Offset_on_opening_bracket_returns_element()
    {
        const string src = "<Button/>";
        var doc = Parse(src);
        Assert.IsType<XamlElement>(doc.FindNodeAt(0));
    }

    [Fact]
    public void Offset_in_attribute_name_returns_attribute()
    {
        //                  0         1         2
        //                  0123456789012345678901
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(4); // inside "Width"
        var attr = Assert.IsType<XamlAttribute>(node);
        Assert.Same(((XamlElement)doc.Roots[0]).Attributes[0], attr);
    }

    [Fact]
    public void Offset_in_attribute_value_returns_value()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(11); // inside "100"
        var value = Assert.IsType<XamlValue>(node);
        Assert.Same(((XamlElement)doc.Roots[0]).Attributes[0].Value, value);
    }

    [Fact]
    public void Offset_on_quote_returns_value()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        // index 9 is the opening quote — still part of value span [9, 14)
        var node = doc.FindNodeAt(9);
        Assert.IsType<XamlValue>(node);
    }

    [Fact]
    public void Offset_on_equals_returns_attribute()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(8); // '='
        Assert.IsType<XamlAttribute>(node);
    }

    [Fact]
    public void Whitespace_between_attributes_falls_back_to_element()
    {
        //                  0         1         2
        //                  01234567890123456789012
        const string src = "<a x=\"1\"  y=\"2\"/>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(8); // first of two spaces between attrs
        Assert.IsType<XamlElement>(node);
    }

    [Fact]
    public void Whitespace_between_children_falls_back_to_parent()
    {
        //                  0         1         2
        //                  01234567890123456789012
        const string src = "<a><b/>   <c/></a>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(8); // inside the "   " between siblings
        var elem = Assert.IsType<XamlElement>(node);
        Assert.Same(doc.Roots[0], elem); // <a> — there is no text child for the whitespace
    }

    [Fact]
    public void Offset_in_text_content_returns_text_node()
    {
        //                  0         1
        //                  012345678901
        const string src = "<a>hello</a>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(5); // inside "hello"
        Assert.IsType<XamlText>(node);
    }

    [Fact]
    public void Offset_in_comment_returns_comment_node()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a><!-- hi --></a>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(8); // inside the comment
        Assert.IsType<XamlComment>(node);
    }

    [Fact]
    public void Offset_in_cdata_returns_cdata_node()
    {
        const string src = "<a><![CDATA[x<y]]></a>";
        var doc = Parse(src);
        var node = doc.FindNodeAt(13); // inside the cdata content
        Assert.IsType<XamlCData>(node);
    }

    [Fact]
    public void Deeply_nested_offset_returns_innermost()
    {
        const string src = "<a><b><c x=\"1\"/></b></a>";
        var doc = Parse(src);

        var a = (XamlElement)doc.Roots[0];
        var b = (XamlElement)a.Children.Single();
        var c = (XamlElement)b.Children.Single();
        var x = c.Attributes.Single();

        // inside "1"
        var hit = doc.FindNodeAt(src.IndexOf('1'));
        Assert.Same(x.Value, hit);

        // inside "c"
        var cName = doc.FindNodeAt(src.IndexOf('c'));
        Assert.Same(c, cName);
    }

    [Fact]
    public void Offset_at_exact_end_of_one_sibling_is_in_the_next()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a/><b/>";
        var doc = Parse(src);
        // <a/> spans [0,4); <b/> spans [4,8). Offset 4 is in <b/>.
        var node = doc.FindNodeAt(4);
        var elem = Assert.IsType<XamlElement>(node);
        Assert.Same(doc.Roots[1], elem);
    }

    [Fact]
    public void Recovery_unclosed_tag_finds_inner_node()
    {
        // <a><b> — <b> is unclosed; both <a> and <b> spans extend to EOF.
        // Offset on 'b' should still return the most specific element (<b>).
        const string src = "<a><b>";
        var doc = Parse(src);
        var a = (XamlElement)doc.Roots[0];
        var b = (XamlElement)a.Children.Single();

        Assert.Same(b, doc.FindNodeAt(4)); // 'b'
        Assert.Same(a, doc.FindNodeAt(1)); // 'a'
    }

    [Fact]
    public void Binary_search_with_many_siblings_finds_correct_one()
    {
        // 50 self-closing siblings inside a root. Pick one in the middle by name.
        var inner = string.Concat(Enumerable.Range(0, 50).Select(i => $"<n{i}/>"));
        var src = "<root>" + inner + "</root>";
        var doc = Parse(src);

        var root = (XamlElement)doc.Roots[0];
        var target = root.Children.OfType<XamlElement>().First(e => doc.TextOf(e.OpenNameSpan) == "n37");

        // pick an offset inside the target's name
        var nameOffset = target.OpenNameSpan.Start + 1;
        Assert.Same(target, doc.FindNodeAt(nameOffset));
    }

    [Fact]
    public void Offset_in_inter_root_whitespace_returns_null()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a/>   <b/>";
        var doc = Parse(src);
        // Roots: <a/> and <b/> only — whitespace between them is dropped.
        Assert.Null(doc.FindNodeAt(5));
    }

    [Fact]
    public void Offset_truly_past_eof_returns_null()
    {
        const string src = "<a/>";
        var doc = Parse(src);
        Assert.Null(doc.FindNodeAt(src.Length + 10));
        Assert.Null(doc.FindNodeAt(src.Length));
    }
}
