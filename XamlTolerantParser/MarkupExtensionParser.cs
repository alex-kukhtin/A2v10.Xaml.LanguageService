using System.Collections.Generic;

namespace XamlTolerantParser;

// Standalone tolerant parser for a single markup extension expression.
// Operates strictly inside the ContentSpan of a XamlValue: never reads past
// _end, never touches the outer tag scanner. Diagnostics are appended to the
// shared list passed in by the main parser.
//
// Position tracking mirrors XamlLexer: \n advances line, \r is swallowed,
// every other char advances column. Attribute values typically sit on one
// line, but multi-line values stay correct.
internal sealed class MarkupExtensionParser
{
    readonly string _src;
    readonly List<XamlDiagnostic> _diags;
    int _pos, _line, _col;
    int _end;

    public MarkupExtensionParser(string source, List<XamlDiagnostic> diagnostics)
    {
        _src = source;
        _diags = diagnostics;
    }

    // Returns a XamlExtension when `contentSpan` opens with an extension, or
    // null when the content is a plain string value. Three cases yield null:
    //   * empty content
    //   * content does not start with '{'
    //   * content starts with the escape sequence '{}' (XAML literal-string
    //     escape — the rest of the value is verbatim text)
    public XamlExtension? TryParse(TextSpan contentSpan)
    {
        if (contentSpan.Length == 0) return null;
        var s = contentSpan.Start;
        var e = contentSpan.End;
        if (_src[s] != '{') return null;
        if (s + 1 < e && _src[s + 1] == '}') return null;

        _pos = s;
        _line = contentSpan.StartLine;
        _col = contentSpan.StartColumn;
        _end = e;

        return ParseExtension();
    }

    XamlExtension ParseExtension()
    {
        // Precondition: _src[_pos] == '{' and _pos < _end.
        var openStart = Snapshot();
        Consume(); // '{'
        var openSpan = SpanSince(openStart);
        var ext = new XamlExtension(openSpan);

        SkipWs();

        // Type name (QName: letters/digits/_/-/./:). Length 0 is tolerated —
        // happens with `{|` at cursor or `{ , Foo=1}`. We still produce the
        // node so completion can see the partial extension.
        var typeStart = Snapshot();
        while (_pos < _end && IsNameChar(_src[_pos]))
            Consume();
        ext.TypeNameSpan = SpanSince(typeStart);
        if (ext.TypeNameSpan.Length == 0)
            _diags.Add(new XamlDiagnostic(openSpan, "Expected markup extension type name."));

        SkipWs();

        if (_pos < _end && _src[_pos] != '}')
            ParseArguments(ext);

        if (_pos < _end && _src[_pos] == '}')
        {
            var closeStart = Snapshot();
            Consume();
            var closeSpan = SpanSince(closeStart);
            ext.CloseBraceSpan = closeSpan;
            ext.Span = TextSpan.FromTo(openSpan, closeSpan);
        }
        else
        {
            _diags.Add(new XamlDiagnostic(openSpan, "Unterminated markup extension."));
            // _pos (and thus `current`) is always at or past the end of the
            // last argument, so this covers the whole parsed region.
            var current = SpanAt(Snapshot());
            ext.Span = TextSpan.FromTo(openSpan, current);
        }

        return ext;
    }

    void ParseArguments(XamlExtension ext)
    {
        while (_pos < _end)
        {
            SkipWs();
            if (_pos >= _end || IsExtensionBarrier(_src[_pos])) return;

            var beforeArgPos = _pos;
            var arg = ParseArgument(ext);
            if (arg != null)
                ext.Arguments.Add(arg);

            SkipWs();

            if (_pos < _end && _src[_pos] == ',')
            {
                Consume();
                continue;
            }
            if (_pos < _end && IsExtensionBarrier(_src[_pos]))
                return;

            // Stuck: neither separator nor close. If we haven't advanced, eat
            // one char to guarantee termination and report a diagnostic.
            if (_pos == beforeArgPos)
            {
                var unkStart = Snapshot();
                if (_pos < _end) Consume();
                _diags.Add(new XamlDiagnostic(SpanSince(unkStart),
                    "Unexpected character in markup extension."));
            }
        }
    }

    XamlExtensionArgument? ParseArgument(XamlExtension ext)
    {
        // Disambiguate named vs positional by a one-shot lookahead: a stretch
        // of name chars followed (across whitespace) by '=' means named.
        int look = _pos;
        while (look < _end && IsNameChar(_src[look])) look++;
        int afterName = look;
        while (look < _end && IsWs(_src[look])) look++;
        bool isNamed = afterName > _pos && look < _end && _src[look] == '=';

        if (isNamed)
            return ParseNamedArgument(ext);
        return ParsePositionalArgument(ext);
    }

    XamlNamedArgument ParseNamedArgument(XamlExtension ext)
    {
        var nameStart = Snapshot();
        while (_pos < _end && IsNameChar(_src[_pos]))
            Consume();
        var nameSpan = SpanSince(nameStart);
        var arg = new XamlNamedArgument(nameSpan) { Parent = ext };

        SkipWs();
        // '=' is guaranteed by ParseArgument's lookahead.
        var eqStart = Snapshot();
        Consume();
        arg.EqualsSpan = SpanSince(eqStart);

        SkipWs();
        var val = ParseValue(arg);
        arg.Value = val;

        arg.Span = val != null
            ? TextSpan.FromTo(nameSpan, val.Span)
            : TextSpan.FromTo(nameSpan, arg.EqualsSpan.Value);
        return arg;
    }

    XamlPositionalArgument? ParsePositionalArgument(XamlExtension ext)
    {
        XamlExtensionValue? val = ParseValue(null);
        if (val == null)
        {
            // Nothing parsed: e.g. ",," sequence. ParseArguments will eat or
            // skip — don't add an empty arg.
            return null;
        }
        var arg = new XamlPositionalArgument(val.Span) { Parent = ext };
        arg.Value = val;
        val.Parent = arg;
        // val.Span already accurate; arg span == val span for positional.
        return arg;
    }

    // Parses one value: nested extension when '{', else a bare string token
    // that runs until ',', '}', or end. Returns null when nothing meaningful
    // is at the cursor (immediate ',', '}', or end).
    XamlExtensionValue? ParseValue(XamlNode? parent)
    {
        if (_pos >= _end) return null;
        var c = _src[_pos];
        if (c == ',' || IsExtensionBarrier(c)) return null;

        if (c == '{')
        {
            // Nested extension. Spec '{}' escape doesn't apply inside an
            // extension value, so always parse — empty type name is fine.
            var nested = ParseExtension();
            if (parent != null) nested.Parent = parent;
            return nested;
        }

        // Bare string value: read until separator. Trim trailing whitespace
        // so spans match user-visible text.
        var start = Snapshot();
        var lastNonWs = Snapshot();
        while (_pos < _end)
        {
            var ch = _src[_pos];
            if (ch == ',' || ch == '{' || IsExtensionBarrier(ch)) break;
            Consume();
            if (!IsWs(ch))
                lastNonWs = Snapshot();
        }
        var span = SpanFromTo(start, lastNonWs);
        if (span.Length == 0) return null;
        var sv = new XamlExtensionStringValue(span);
        if (parent != null) sv.Parent = parent;
        return sv;
    }

    // -------- cursor helpers --------

    readonly record struct Cursor(int Pos, int Line, int Col);

    Cursor Snapshot() => new(_pos, _line, _col);

    void Consume()
    {
        var ch = _src[_pos];
        _pos++;
        if (ch == '\n') { _line++; _col = 0; }
        else if (ch != '\r') _col++;
    }

    void SkipWs()
    {
        while (_pos < _end && IsWs(_src[_pos]))
            Consume();
    }

    TextSpan SpanSince(Cursor start) =>
        new(start.Pos, _pos - start.Pos, start.Line, start.Col, _line, _col);

    TextSpan SpanAt(Cursor at) =>
        new(at.Pos, 0, at.Line, at.Col, at.Line, at.Col);

    TextSpan SpanFromTo(Cursor from, Cursor to) =>
        new(from.Pos, to.Pos - from.Pos, from.Line, from.Col, to.Line, to.Col);

    static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';

    // Hard stop characters that can never appear in a well-formed markup
    // extension. When the attribute value itself is unterminated the lexer
    // hands us a ContentSpan that may run past `>` and on to EOF; without
    // this guard the mini-parser would swallow the rest of the tag (and
    // potentially the rest of the document) into a giant string value.
    // Treating '<' and '>' as barriers makes the unterminated-extension span
    // stop at the surrounding tag's boundary, which is what diagnostics and
    // FindNodeAt expect.
    //
    // The matching attribute quote (' / ") is intentionally NOT a barrier:
    // when ContentSpan is well-formed it can't contain the outer quote, and
    // when it isn't, the inner quote could legitimately be a string-format
    // delimiter inside an argument value.
    static bool IsExtensionBarrier(char c) => c == '}' || c == '<' || c == '>';
}
