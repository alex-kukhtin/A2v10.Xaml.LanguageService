using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class ContextAtTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    // Single-line: column == legacy offset.
    private static XamlPositionContext At(XamlDocument doc, int column) => doc.ContextAt(0, column);

    [Fact]
    public void Empty_document_is_Outside()
    {
        var doc = Parse("");
        var ctx = Assert.IsType<OutsideContext>(At(doc, 0));
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void Between_roots_whitespace_is_Outside()
    {
        const string src = "<a/>   <b/>";
        var doc = Parse(src);
        Assert.IsType<OutsideContext>(At(doc, 5));
    }

    [Fact]
    public void Inside_tag_name_is_TagName()
    {
        //                  0123456789
        const string src = "<Button/>";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagNameContext>(At(doc, 3)); // inside "Button"
        Assert.Same(doc.Roots[0], ctx.Element);
        // ContainerElement is parent — root has no parent.
        Assert.Null(ctx.ContainerElement);
    }

    [Fact]
    public void Right_after_tag_name_with_no_whitespace_is_still_TagName()
    {
        //                  0123456
        const string src = "<Button>";
        var doc = Parse(src);
        // Column 7 is right after 'n' — left char is name-char → TagName.
        Assert.IsType<TagNameContext>(At(doc, 7));
    }

    [Fact]
    public void Just_after_less_than_with_no_name_yet_is_TagName_with_null_element()
    {
        // Trigger character moment: user typed '<' and completion fires.
        const string src = "<";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagNameContext>(At(doc, 1));
        Assert.Null(ctx.Element);
    }

    [Fact]
    public void Nested_partial_tag_resolves_to_partial_element()
    {
        //                  0123456789012
        const string src = "<a><Butt";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagNameContext>(At(doc, 8)); // right after 'Butt'
        Assert.NotNull(ctx.Element);
        Assert.Equal("Butt", doc.TextOf(ctx.Element!.OpenNameSpan));
        // ContainerElement is the parent <a>.
        Assert.Same(doc.Roots[0], ctx.ContainerElement);
    }

    [Fact]
    public void Inside_close_tag_name_is_EndTagName()
    {
        const string src = "<a></a>";
        var doc = Parse(src);
        // column 5 is the 'a' of '</a>'
        var ctx = Assert.IsType<EndTagNameContext>(At(doc, 5));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Unterminated_close_tag_classifies_as_EndTagName()
    {
        // No closing '>' — name still recognisable by the probe.
        const string src = "<a></a";
        var doc = Parse(src);
        // column 6 is right after 'a'
        var ctx = Assert.IsType<EndTagNameContext>(At(doc, 6));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Unmatched_close_tag_has_null_element()
    {
        // '</Butt' doesn't match anything on the open stack.
        const string src = "<a></Butt";
        var doc = Parse(src);
        var ctx = Assert.IsType<EndTagNameContext>(At(doc, 9)); // after 't' of "Butt"
        Assert.Null(ctx.Element);
    }

    [Fact]
    public void Whitespace_inside_open_tag_is_TagInterior()
    {
        //                  012345678901
        const string src = "<a x=\"1\" />";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagInteriorContext>(At(doc, 8)); // space between attribute and '/'
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Inside_attribute_name_is_AttributeName()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        var ctx = Assert.IsType<AttributeNameContext>(At(doc, 4)); // inside "Width"
        Assert.Equal("Width", doc.TextOf(ctx.Attribute.NameSpan));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Inside_attribute_value_is_AttributeValue()
    {
        const string src = "<a Width=\"100\"/>";
        var doc = Parse(src);
        var ctx = Assert.IsType<AttributeValueContext>(At(doc, 11)); // inside "100"
        Assert.NotNull(ctx.Value);
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Right_after_equals_with_no_value_is_AttributeValue_with_null_Value()
    {
        // Cursor at end of '<a b=' — no value emitted yet.
        const string src = "<a b=";
        var doc = Parse(src);
        var ctx = Assert.IsType<AttributeValueContext>(At(doc, 5));
        Assert.Null(ctx.Value);
    }

    [Fact]
    public void Less_than_inside_attribute_value_is_still_AttributeValue()
    {
        // The '<' inside the string body must not trigger TagName.
        const string src = "<a Width=\"some<text\"/>";
        var doc = Parse(src);
        Assert.IsType<AttributeValueContext>(At(doc, 19)); // inside "some<text", right after 't'
    }

    [Fact]
    public void Between_children_is_ElementContent()
    {
        //                  0         1
        //                  0123456789012345
        const string src = "<a><b/>   <c/></a>";
        var doc = Parse(src);
        var ctx = Assert.IsType<ElementContentContext>(At(doc, 8)); // in the "   " between siblings
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Empty_element_body_is_ElementContent()
    {
        const string src = "<a></a>";
        var doc = Parse(src);
        Assert.IsType<ElementContentContext>(At(doc, 3));
    }

    [Fact]
    public void Inside_text_content_is_Text()
    {
        const string src = "<a>hello</a>";
        var doc = Parse(src);
        var ctx = Assert.IsType<TextContext>(At(doc, 5));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Inside_comment_is_Comment()
    {
        const string src = "<a><!-- hi --></a>";
        var doc = Parse(src);
        var ctx = Assert.IsType<CommentContext>(At(doc, 8));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Inside_cdata_is_CData()
    {
        const string src = "<a><![CDATA[x<y]]></a>";
        var doc = Parse(src);
        Assert.IsType<CDataContext>(At(doc, 13));
    }

    [Fact]
    public void ContainerElement_in_tag_name_state_is_parent()
    {
        const string src = "<root><Butt";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagNameContext>(At(doc, 11)); // inside the partial 'Butt'
        // Element is the partial <Butt>; ContainerElement is its parent <root>.
        Assert.NotSame(ctx.Element, ctx.ContainerElement);
        Assert.Same(doc.Roots[0], ctx.ContainerElement);
    }

    [Fact]
    public void Diagnostics_at_position_are_surfaced()
    {
        // Unclosed tag — diagnostic covers the open tag's name span.
        const string src = "<a>";
        var doc = Parse(src);
        var ctx = At(doc, 1); // inside 'a'
        Assert.NotEmpty(ctx.Diagnostics);
        Assert.Contains(ctx.Diagnostics, d => d.Message.Contains("Unclosed"));
    }

    [Fact]
    public void Diagnostics_filtered_to_those_overlapping_position()
    {
        // Cursor outside the diagnostic's span — no local diagnostic.
        const string src = "<a>   ";
        var doc = Parse(src);
        var ctx = At(doc, 5); // in the trailing whitespace of unclosed element body
        // The 'Unclosed' diagnostic is on OpenNameSpan = [1,2), not at column 5.
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void Multiline_TagName_on_second_line()
    {
        const string src = "<a>\n  <Butt";
        var doc = Parse(src);
        // (1, 10) is right after the last 't' of "Butt".
        var ctx = Assert.IsType<TagNameContext>(doc.ContextAt(1, 10));
        Assert.NotNull(ctx.Element);
        Assert.Equal("Butt", doc.TextOf(ctx.Element!.OpenNameSpan));
    }
}
