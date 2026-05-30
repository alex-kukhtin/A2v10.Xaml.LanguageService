using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

// EditAt models onTypeFormatting: the trigger character is ALREADY in the source
// and the caret sits right after it. Each `src` below is the post-keystroke
// buffer; the column passed to EditAt is the caret (one past the typed char).
public class EditAtTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    // ---- '/' completes a self-closing tag --------------------------------

    [Fact]
    public void Slash_after_tag_name_completes_to_self_close()
    {
        //                  01234567
        const string src = "<Button/";
        var doc = Parse(src);
        var edit = doc.EditAt(0, 8, '/');
        Assert.NotNull(edit);
        Assert.Equal("/>", edit!.NewText);
        // Replace covers just the '/', range END is the caret -> caret after '>'.
        Assert.Equal(7, edit.Replace.Start);
        Assert.Equal(1, edit.Replace.Length);
        Assert.Equal(7, edit.Replace.StartColumn);
        Assert.Equal(8, edit.Replace.EndColumn);
    }

    [Fact]
    public void Slash_after_attributes_completes_to_self_close()
    {
        //                  0         1
        //                  012345678901234567
        const string src = "<Button Width=\"1\"/";
        var doc = Parse(src);
        var edit = doc.EditAt(0, src.Length, '/');
        Assert.NotNull(edit);
        Assert.Equal("/>", edit!.NewText);
    }

    [Fact]
    public void Slash_already_followed_by_gt_is_noop()
    {
        // "<Button/>" with the caret between '/' and '>': '/>' already formed and
        // nothing follows -> nothing to collapse.
        const string src = "<Button/>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 8, '/'));
    }

    // ---- '/' collapses a now-redundant close tag -------------------------

    [Fact]
    public void Slash_with_matching_close_tag_collapses_it()
    {
        //                  0123456789012345
        // "<Grid|></Grid>" after typing '/': caret between '/' and '>' at col 6.
        const string src = "<Grid/></Grid>";
        var doc = Parse(src);
        var edit = doc.EditAt(0, 6, '/');
        Assert.NotNull(edit);
        Assert.Equal("", edit!.NewText);
        // Deletes the "</Grid>" that stands right after the '>'.
        Assert.Equal(7, edit.Replace.Start);
        Assert.Equal(7, edit.Replace.Length); // "</Grid>".Length
        Assert.Equal(7, edit.Replace.StartColumn);
        Assert.Equal(14, edit.Replace.EndColumn);
    }

    [Fact]
    public void Slash_collapse_requires_name_match()
    {
        // A partial child before the parent's close tag must NOT eat </StackPanel>.
        const string src = "<StackPanel><Grid/></StackPanel>";
        var doc = Parse(src);
        // caret between '/' and '>' of <Grid/> — '/' at index 17, caret col 18.
        Assert.Null(doc.EditAt(0, 18, '/'));
    }

    [Fact]
    public void Slash_collapse_requires_literal_adjacency_no_whitespace()
    {
        // Strict: whitespace between '>' and the close tag -> rule does not fire.
        const string src = "<Grid/> </Grid>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 6, '/'));
    }

    [Fact]
    public void Slash_collapse_requires_content_empty_via_adjacency()
    {
        // Content right after '>' means no literal "</" follows -> no collapse.
        const string src = "<Grid/>x</Grid>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 6, '/'));
    }

    [Fact]
    public void Slash_inside_attribute_value_is_noop()
    {
        //                  0123456789
        const string src = "<a x=\"a/b\"";
        var doc = Parse(src);
        // caret after the '/' at index 7
        Assert.Null(doc.EditAt(0, 8, '/'));
    }

    [Fact]
    public void Slash_inside_text_content_is_noop()
    {
        //                  012345
        const string src = "<a>a/";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 5, '/'));
    }

    // ---- '>' inserts a matching close tag --------------------------------

    [Fact]
    public void Gt_after_tag_name_inserts_close_tag()
    {
        //                  01234567
        const string src = "<Button>";
        var doc = Parse(src);
        var edit = doc.EditAt(0, 8, '>');
        Assert.NotNull(edit);
        Assert.Equal("</Button>", edit!.NewText);
        // Empty Replace at the caret -> caret stays to the left (between tags).
        Assert.Equal(8, edit.Replace.Start);
        Assert.Equal(0, edit.Replace.Length);
        Assert.Equal(8, edit.Replace.StartColumn);
        Assert.Equal(8, edit.Replace.EndColumn);
    }

    [Fact]
    public void Gt_after_attributes_inserts_close_tag()
    {
        const string src = "<Button Width=\"10\">";
        var doc = Parse(src);
        var edit = doc.EditAt(0, src.Length, '>');
        Assert.NotNull(edit);
        Assert.Equal("</Button>", edit!.NewText);
    }

    [Fact]
    public void Gt_on_nested_child_inserts_child_close_tag()
    {
        //                  012345
        const string src = "<a><b>";
        var doc = Parse(src);
        var edit = doc.EditAt(0, 6, '>');
        Assert.NotNull(edit);
        Assert.Equal("</b>", edit!.NewText);
    }

    [Fact]
    public void Gt_on_self_closing_tag_is_noop()
    {
        // "<a/>" — the '>' belongs to '/>', nothing to close.
        const string src = "<a/>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 4, '>'));
    }

    [Fact]
    public void Gt_when_close_tag_already_present_is_noop()
    {
        // Re-typing the open tag's '>' in an already-closed element.
        const string src = "<a></a>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 3, '>'));
    }

    [Fact]
    public void Gt_inside_text_content_is_noop()
    {
        //                  012345
        const string src = "<a>2>";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 5, '>'));
    }

    [Fact]
    public void Gt_inside_attribute_value_is_noop()
    {
        //                  012345678
        const string src = "<a x=\"1>\"";
        var doc = Parse(src);
        // caret after the '>' at index 7
        Assert.Null(doc.EditAt(0, 8, '>'));
    }

    // ---- multi-line & misc ----------------------------------------------

    [Fact]
    public void Gt_inserts_close_tag_on_second_line()
    {
        const string src = "<Page>\n  <Button>";
        var doc = Parse(src);
        // line 1 = "  <Button>"; '>' at column 9, caret at column 10.
        var edit = doc.EditAt(1, 10, '>');
        Assert.NotNull(edit);
        Assert.Equal("</Button>", edit!.NewText);
        Assert.Equal(1, edit.Replace.StartLine);
        Assert.Equal(10, edit.Replace.StartColumn);
        Assert.Equal(0, edit.Replace.Length);
    }

    [Fact]
    public void Non_trigger_character_is_noop()
    {
        const string src = "<Button";
        var doc = Parse(src);
        Assert.Null(doc.EditAt(0, 7, 'n'));
    }

    [Fact]
    public void Trigger_at_document_start_is_noop()
    {
        var doc = Parse(">");
        Assert.Null(doc.EditAt(0, 0, '>'));
    }
}
