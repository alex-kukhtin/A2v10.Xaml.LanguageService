using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class LineMapTests
{
    [Fact]
    public void Empty_source_has_one_line()
    {
        var map = new LineMap("");
        Assert.Equal(1, map.LineCount);
        Assert.Equal((0, 0), map.ToLineColumn(0));
    }

    [Fact]
    public void Single_line_columns_are_offsets()
    {
        var map = new LineMap("abcdef");
        Assert.Equal((0, 0), map.ToLineColumn(0));
        Assert.Equal((0, 3), map.ToLineColumn(3));
        Assert.Equal((0, 6), map.ToLineColumn(6)); // at EOF
    }

    [Fact]
    public void Lf_separators_advance_line()
    {
        //         0123 4567 8
        // src =  "ab\ncd\nef"
        var map = new LineMap("ab\ncd\nef");
        Assert.Equal((0, 0), map.ToLineColumn(0));
        Assert.Equal((0, 1), map.ToLineColumn(1));
        Assert.Equal((0, 2), map.ToLineColumn(2)); // the \n
        Assert.Equal((1, 0), map.ToLineColumn(3)); // 'c'
        Assert.Equal((1, 1), map.ToLineColumn(4));
        Assert.Equal((1, 2), map.ToLineColumn(5)); // the \n
        Assert.Equal((2, 0), map.ToLineColumn(6)); // 'e'
        Assert.Equal((2, 2), map.ToLineColumn(8));
        Assert.Equal(3, map.LineCount);
    }

    [Fact]
    public void Crlf_separators_count_only_the_lf()
    {
        //                  0   1 2  3   4 5
        // src = "a\r\nb\r\nc"
        var map = new LineMap("a\r\nb\r\nc");
        Assert.Equal((0, 0), map.ToLineColumn(0)); // 'a'
        Assert.Equal((0, 1), map.ToLineColumn(1)); // \r
        Assert.Equal((0, 2), map.ToLineColumn(2)); // \n
        Assert.Equal((1, 0), map.ToLineColumn(3)); // 'b'
        Assert.Equal((2, 0), map.ToLineColumn(6)); // 'c'
        Assert.Equal(3, map.LineCount);
    }

    [Fact]
    public void Trailing_newline_creates_extra_empty_line()
    {
        var map = new LineMap("abc\n");
        Assert.Equal(2, map.LineCount);
        Assert.Equal((1, 0), map.ToLineColumn(4)); // past the \n, at EOF
    }

    [Fact]
    public void Offset_past_eof_is_clamped()
    {
        var map = new LineMap("abc");
        Assert.Equal((0, 3), map.ToLineColumn(100));
    }

    [Fact]
    public void Negative_offset_is_clamped_to_origin()
    {
        var map = new LineMap("abc\nde");
        Assert.Equal((0, 0), map.ToLineColumn(-5));
    }

    [Fact]
    public void Binary_search_correct_on_many_lines()
    {
        // 1000 lines of "x"
        var src = string.Join('\n', Enumerable.Range(0, 1000).Select(_ => "x"));
        var map = new LineMap(src);

        // line N starts at offset N * 2 (one 'x' + one '\n' each line, except last)
        Assert.Equal((0, 0), map.ToLineColumn(0));
        Assert.Equal((1, 0), map.ToLineColumn(2));
        Assert.Equal((500, 0), map.ToLineColumn(1000));
        Assert.Equal((500, 1), map.ToLineColumn(1001)); // the 'x' on line 500
        Assert.Equal((999, 1), map.ToLineColumn(1999)); // last 'x'
        Assert.Equal(1000, map.LineCount);
    }

    [Fact]
    public void Blank_lines_in_the_middle()
    {
        //                  0 1 2 3 4
        var map = new LineMap("a\n\n\nb");
        Assert.Equal((0, 0), map.ToLineColumn(0)); // 'a'
        Assert.Equal((1, 0), map.ToLineColumn(2)); // first blank
        Assert.Equal((2, 0), map.ToLineColumn(3)); // second blank
        Assert.Equal((3, 0), map.ToLineColumn(4)); // 'b'
        Assert.Equal(4, map.LineCount);
    }
}
