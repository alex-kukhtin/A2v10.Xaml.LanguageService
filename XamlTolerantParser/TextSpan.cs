namespace XamlTolerantParser;

// Carries both offset-based and line/column-based positions.
// Offsets are useful for cheap Source.Substring; line/column matches LSP
// Position directly so handlers don't need to convert.
// Lines and columns are 0-based; column counts UTF-16 code units within the
// line (raw char index, no tab expansion). End is exclusive on all three axes.
public readonly record struct TextSpan(
    int Start, int Length,
    int StartLine, int StartColumn,
    int EndLine, int EndColumn)
{
    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(int line, int column)
    {
        if (line < StartLine || line > EndLine) return false;
        if (line == StartLine && column < StartColumn) return false;
        if (line == EndLine && column >= EndColumn) return false;
        return true;
    }

    public override string ToString() =>
        $"[{Start}..{End}) ({StartLine},{StartColumn})..({EndLine},{EndColumn})";

    // Span from the start of `from` to the end of `to`. Used by the parser
    // to merge token spans into element/attribute spans.
    public static TextSpan FromTo(TextSpan from, TextSpan to) =>
        new(from.Start, to.End - from.Start,
            from.StartLine, from.StartColumn,
            to.EndLine, to.EndColumn);

    // Span from the start of `from` up to (but not including) the start of `to`.
    // Used for recovery — extend an unclosed element up to the next sibling.
    public static TextSpan FromToStart(TextSpan from, TextSpan to) =>
        new(from.Start, to.Start - from.Start,
            from.StartLine, from.StartColumn,
            to.StartLine, to.StartColumn);
}
