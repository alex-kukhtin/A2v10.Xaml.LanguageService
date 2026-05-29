using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

// Binary search over a list of items sorted by TextSpan.Start with non-overlapping spans.
// Contract is the responsibility of the caller (parser produces sibling spans in order).
internal static class SpanSearch
{
    public static Int32 FindContaining<T>(IReadOnlyList<T> sorted, Int32 line, Int32 column, Func<T, TextSpan> getSpan)
    {
        Int32 lo = 0, hi = sorted.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var span = getSpan(sorted[mid]);
            if (IsBefore(line, column, span))
                hi = mid - 1;
            else if (IsAtOrAfter(line, column, span))
                lo = mid + 1;
            else
                return mid;
        }
        return -1;
    }

    private static Boolean IsBefore(Int32 line, Int32 column, TextSpan s) =>
        line < s.StartLine || (line == s.StartLine && column < s.StartColumn);

    private static Boolean IsAtOrAfter(Int32 line, Int32 column, TextSpan s) =>
        line > s.EndLine || (line == s.EndLine && column >= s.EndColumn);
}
