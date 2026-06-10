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
        var ctx = Assert.IsType<ChildTagNameContext>(At(doc, 8)); // right after 'Butt'
        Assert.NotNull(ctx.Element);
        Assert.Equal("Butt", doc.TextOf(ctx.Element!.OpenNameSpan));
        // Container is the parent <a>.
        Assert.Same(doc.Roots[0], ctx.Container);
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

    // --- Probing the real mid-typing shapes (partial name-only attribute) ---

    [Fact]
    public void Partial_attr_name_at_eof_is_AttributeName()
    {
        //                  012345678901
        const string src = "<Button Wi";
        var doc = Parse(src);
        // Column 10 — right after "Wi", the exclusive end of the attribute span.
        Assert.IsType<AttributeNameContext>(At(doc, 10));
    }

    [Fact]
    public void Partial_attr_name_before_selfclose_is_AttributeName()
    {
        //                  0123456789012345
        const string src = "<Button Wi />";
        var doc = Parse(src);
        // Column 10 — right after "Wi", before the trailing space.
        Assert.IsType<AttributeNameContext>(At(doc, 10));
    }

    [Fact]
    public void Empty_interior_slot_is_TagInterior_not_AttributeName()
    {
        //                  0123456789012
        const string src = "<Button  />";
        var doc = Parse(src);
        // Column 8 — the second space, no name being typed.
        Assert.IsType<TagInteriorContext>(At(doc, 8));
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

    // --- Content of an UNCLOSED element (the mid-typing buffer state) ---

    [Fact]
    public void Content_whitespace_of_unclosed_element_is_ElementContent()
    {
        // The open tag IS closed; the cursor sits in content whitespace.
        const string src = "<Grid>\n  \n";
        var doc = Parse(src);
        var ctx = Assert.IsType<ElementContentContext>(doc.ContextAt(1, 1));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Content_whitespace_of_unclosed_element_at_exact_eof_is_ElementContent()
    {
        // No trailing newline: the cursor sits at the exclusive end of the
        // unclosed element's span. The element owns its trailing edge — there
        // is nothing after it for the boundary position to belong to (same
        // rule as an unclosed markup extension).
        const string src = "<Grid>\n  ";
        var doc = Parse(src);
        var ctx = Assert.IsType<ElementContentContext>(doc.ContextAt(1, 2));
        Assert.Same(doc.Roots[0], ctx.Element);
    }

    [Fact]
    public void Right_after_open_tag_of_unclosed_child_at_eof_is_ElementContent()
    {
        //                  0123456
        const string src = "<a><b>";
        var doc = Parse(src);
        var ctx = Assert.IsType<ElementContentContext>(At(doc, 6));
        Assert.Equal("b", doc.TextOf(ctx.Element.OpenNameSpan));
    }

    [Fact]
    public void Unterminated_open_tag_trailing_edge_at_eof_stays_Outside()
    {
        // No '>' yet — the whole element is still its open tag, so the
        // trailing edge is the (quiet) attribute zone, not content. The
        // trailing-edge retry must NOT pull it in.
        //                  01234567
        const string src = "<Button ";
        var doc = Parse(src);
        Assert.IsType<OutsideContext>(At(doc, 8));
    }

    [Fact]
    public void At_a_childs_opening_angle_is_TagInterior_of_child()
    {
        // Cursor exactly on '<' of a child: the position is inside the child's
        // OpenTagSpan. Deliberately quiet (TagInterior offers nothing) — the
        // old forward-scan classified it as the CHILD's content, offering the
        // child's children one position before the child even starts.
        const string src = "<a><b/></a>";
        var doc = Parse(src);
        var ctx = Assert.IsType<TagInteriorContext>(At(doc, 3));
        Assert.Equal("b", doc.TextOf(ctx.Element.OpenNameSpan));
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
        var ctx = Assert.IsType<ChildTagNameContext>(At(doc, 11)); // inside the partial 'Butt'
        // Element is the partial <Butt>; Container is its parent <root>.
        Assert.NotSame(ctx.Element, ctx.Container);
        Assert.Same(doc.Roots[0], ctx.Container);
    }

    [Fact]
    public void Partial_child_before_close_tag_is_ChildTagName_with_container()
    {
        // The reported case: a partial child tag sitting before the parent's
        // close tag must carry the parent as its container, not fall back to
        // root scope.
        const string src = "<Page>\n   <But\n</Page>";
        var doc = Parse(src);
        var ctx = Assert.IsType<ChildTagNameContext>(doc.ContextAt(1, 7)); // after "But"
        Assert.Equal("But", doc.TextOf(ctx.Element!.OpenNameSpan));
        Assert.Same(doc.Roots[0], ctx.Container); // <Page>
    }

    [Fact]
    public void Just_after_less_than_inside_element_is_ChildTagName_with_null_element()
    {
        // '<' typed inside a parent, no name yet — no partial node exists, but
        // the container is still the enclosing element.
        const string src = "<a><";
        var doc = Parse(src);
        var ctx = Assert.IsType<ChildTagNameContext>(At(doc, 4));
        Assert.Null(ctx.Element);
        Assert.Same(doc.Roots[0], ctx.Container); // <a>
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
        var ctx = Assert.IsType<ChildTagNameContext>(doc.ContextAt(1, 10));
        Assert.NotNull(ctx.Element);
        Assert.Equal("Butt", doc.TextOf(ctx.Element!.OpenNameSpan));
        Assert.Same(doc.Roots[0], ctx.Container);
    }
}
