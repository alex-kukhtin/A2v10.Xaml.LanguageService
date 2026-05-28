using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

// Binary search over a list of items sorted by TextSpan.Start with non-overlapping spans.
// Contract is the responsibility of the caller (parser produces sibling spans in order).
internal static class SpanSearch
{
    public static Int32 FindContaining<T>(IReadOnlyList<T> sorted, Int32 offset, Func<T, TextSpan> getSpan)
    {
        Int32 lo = 0, hi = sorted.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var span = getSpan(sorted[mid]);
            if (offset < span.Start)
                hi = mid - 1;
            else if (offset >= span.End)
                lo = mid + 1;
            else
                return mid;
        }
        return -1;
    }
}
