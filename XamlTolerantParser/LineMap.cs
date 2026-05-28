using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

// Offset ↔ (line, column) translation for a source string.
// Both line and column are 0-based, matching the LSP convention.
// Column counts UTF-16 code units (raw char index within the line) —
// good enough for ASCII / BMP. Astral plane characters would need
// surrogate-pair handling; revisit if vxaml files contain them.
public sealed class LineMap
{
    private readonly Int32[] _lineStarts;
    private readonly Int32 _length;

    public LineMap(String source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        _length = source.Length;
        var starts = new List<Int32>(Math.Max(8, source.Length / 40)) { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
                starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
    }

    public Int32 LineCount => _lineStarts.Length;

    public (Int32 line, Int32 column) ToLineColumn(Int32 offset)
    {
        if (offset < 0)
            offset = 0;
        else if (offset > _length)
            offset = _length;

        var line = FindLine(offset);
        return (line, offset - _lineStarts[line]);
    }

    // Inverse of ToLineColumn. Out-of-range line/column are clamped to the
    // document bounds so callers never have to pre-validate LSP positions.
    public Int32 ToOffset(Int32 line, Int32 column)
    {
        if (line < 0)
            return 0;
        if (line >= _lineStarts.Length)
            return _length;
        var lineStart = _lineStarts[line];
        var lineEnd = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : _length;
        var offset = lineStart + (column < 0 ? 0 : column);
        return offset > lineEnd ? lineEnd : offset;
    }

    private Int32 FindLine(Int32 offset)
    {
        // Largest index i with _lineStarts[i] <= offset.
        Int32 lo = 0, hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo + 1) >> 1);
            if (_lineStarts[mid] <= offset)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}
