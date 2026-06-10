using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlParser
{
    readonly string _source;
    readonly XamlLexer _lex;
    readonly Stack<XamlElement> _open = new();
    readonly List<XamlNode> _roots = [];
    readonly List<XamlDiagnostic> _diagnostics = [];

    XamlParser(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lex = new XamlLexer(source);
    }

    public static XamlDocument Parse(string source) =>
        new XamlParser(source).ParseDocument();

    XamlDocument ParseDocument()
    {
        Token eofTok;
        while (true)
        {
            var tok = _lex.NextToken();
            if (tok.Kind == TokenKind.EndOfFile)
            {
                eofTok = tok;
                break;
            }
            HandleToken(tok);
        }
        FinalizeOpenElements(eofTok.Span);
        var (clr, lang, asms) = XmlnsExtraction.Extract(_roots, _source);
        return new XamlDocument(_source, _roots, _diagnostics, clr, lang, asms, _lex.GetLineStarts());
    }

    void HandleToken(Token tok)
    {
        switch (tok.Kind)
        {
            case TokenKind.StartTagOpen:
                ParseStartTag(tok);
                break;
            case TokenKind.EndTagOpen:
                ParseEndTag(tok);
                break;
            case TokenKind.Content:
                // Whitespace-only character data is dropped: it is noise for a language
                // service (no completion / hover / diagnostic uses it) and triples the
                // tree size on prettified files. Text with significant content is kept
                // as-is, including any surrounding whitespace inside it.
                if (!IsWhitespaceOnly(tok.Span))
                    Add(new XamlText(tok.Span));
                break;
            case TokenKind.Comment:
                Add(new XamlComment(tok.Span));
                if (tok.IsUnterminated)
                    _diagnostics.Add(new XamlDiagnostic(tok.Span, "Unterminated comment."));
                break;
            case TokenKind.CData:
                Add(new XamlCData(tok.Span));
                if (tok.IsUnterminated)
                    _diagnostics.Add(new XamlDiagnostic(tok.Span, "Unterminated CDATA section."));
                break;
            case TokenKind.Unknown:
                _diagnostics.Add(new XamlDiagnostic(tok.Span, "Unexpected character."));
                break;
            default:
                _diagnostics.Add(new XamlDiagnostic(tok.Span, $"Unexpected token '{tok.Kind}'."));
                break;
        }
    }

    void Add(XamlNode node)
    {
        if (_open.Count == 0)
        {
            _roots.Add(node);
        }
        else
        {
            var parent = _open.Peek();
            node.Parent = parent;
            parent.Children.Add(node);
        }
    }

    void ParseStartTag(Token openTok)
    {
    // Manual tail-call elimination. The StartTagOpen recovery case below used to
    // recurse (`ParseStartTag(tok); return;`) — a pure tail call, but the JIT does
    // not guarantee eliminating it, so a pathological run of `<a <a <a ...` (a
    // binary file opened as .vxaml) could overflow the stack. The goto IS that
    // tail call, spelled as a jump: `openTok = tok; goto restart;` re-enters the
    // method body with the new token, byte-for-byte the same semantics, constant
    // stack. Do not "clean this up" back into recursion or a while+flag — the
    // label is the cheapest equivalent form.
    restart:
        var name = _lex.NextToken();
        if (name.Kind != TokenKind.ElementName)
        {
            _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Expected element name."));
            _lex.Back();
            return;
        }

        var elem = new XamlElement(name.Span)
        {
            Span = TextSpan.FromTo(openTok.Span, name.Span),
        };
        Add(elem);

        while (true)
        {
            var tok = _lex.NextToken();
            switch (tok.Kind)
            {
                case TokenKind.AttributeName:
                    ParseAttribute(elem, tok);
                    break;

                case TokenKind.TagClose:
                    elem.Span = TextSpan.FromTo(elem.Span, tok.Span);
                    elem.OpenTagSpan = elem.Span;
                    _open.Push(elem);
                    return;

                case TokenKind.SelfTagClose:
                    elem.IsSelfClosing = true;
                    elem.Span = TextSpan.FromTo(elem.Span, tok.Span);
                    elem.OpenTagSpan = elem.Span;
                    return;

                case TokenKind.EndOfFile:
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Unclosed tag."));
                    elem.Span = TextSpan.FromTo(elem.Span, tok.Span);
                    _lex.Back();
                    return;

                case TokenKind.StartTagOpen:
                    // The current tag never reached '>'. Close it as a sibling (no push to _open),
                    // then process the new tag — the eliminated tail call (see `restart` above).
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Tag is not closed."));
                    elem.Span = TextSpan.FromToStart(elem.Span, tok.Span);
                    openTok = tok;
                    goto restart;

                case TokenKind.EndTagOpen:
                    // Same idea, but the next thing is somebody's close tag — let the document loop handle it.
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Tag is not closed."));
                    elem.Span = TextSpan.FromToStart(elem.Span, tok.Span);
                    _lex.Back();
                    return;

                case TokenKind.Unknown:
                    _diagnostics.Add(new XamlDiagnostic(tok.Span, "Unexpected character inside tag."));
                    break;

                default:
                    _diagnostics.Add(new XamlDiagnostic(tok.Span, $"Unexpected token '{tok.Kind}' inside tag."));
                    break;
            }
        }
    }

    void ParseAttribute(XamlElement elem, Token nameTok)
    {
        var attr = new XamlAttribute(nameTok.Span) { Parent = elem };
        elem.Attributes.Add(attr);

        var next = _lex.NextToken();
        if (next.Kind != TokenKind.Equals)
        {
            _diagnostics.Add(new XamlDiagnostic(nameTok.Span, "Attribute requires a value."));
            _lex.Back();
            return;
        }
        attr.EqualsSpan = next.Span;

        var valTok = _lex.NextToken();
        if (valTok.Kind != TokenKind.AttributeValue)
        {
            _diagnostics.Add(new XamlDiagnostic(next.Span, "Expected attribute value."));
            _lex.Back();
            attr.Span = TextSpan.FromTo(nameTok.Span, next.Span);
            return;
        }

        var value = MakeValue(valTok);
        value.Parent = attr;
        attr.Value = value;
        attr.Span = TextSpan.FromTo(nameTok.Span, valTok.Span);
    }

    XamlValue MakeValue(Token valTok)
    {
        // The opening and closing quotes are single ASCII characters that
        // cannot be newlines, so the content span is the token span shrunk
        // by one column on each side (terminated case) or just on the left
        // (unterminated — there is no closing quote to skip).
        var s = valTok.Span;
        char quote = _source[s.Start];

        int contentStartOffset = s.Start + 1;
        int contentStartLine = s.StartLine;
        int contentStartCol = s.StartColumn + 1;

        int contentEndOffset, contentEndLine, contentEndCol;
        if (valTok.IsUnterminated)
        {
            contentEndOffset = s.End;
            contentEndLine = s.EndLine;
            contentEndCol = s.EndColumn;
        }
        else
        {
            contentEndOffset = s.End - 1;
            contentEndLine = s.EndLine;
            contentEndCol = s.EndColumn - 1;
        }

        var contentSpan = new TextSpan(
            contentStartOffset, contentEndOffset - contentStartOffset,
            contentStartLine, contentStartCol,
            contentEndLine, contentEndCol);
        var value = new XamlValue(s, contentSpan, quote, valTok.IsUnterminated);

        // Promote `{ ... }` content to a structured XamlExtension. The
        // mini-parser is self-contained: it reads only inside ContentSpan
        // and appends diagnostics to the same list.
        var ext = new MarkupExtensionParser(_source, _diagnostics).TryParse(contentSpan);
        if (ext != null)
        {
            ext.Parent = value;
            value.Extension = ext;
        }
        return value;
    }

    void ParseEndTag(Token endTok)
    {
        var name = _lex.NextToken();
        if (name.Kind != TokenKind.ElementName)
        {
            _diagnostics.Add(new XamlDiagnostic(endTok.Span, "Expected element name in end tag."));
            _lex.Back();
            return;
        }

        var close = _lex.NextToken();
        TextSpan endRef;
        if (close.Kind == TokenKind.TagClose)
        {
            endRef = close.Span;
        }
        else
        {
            _diagnostics.Add(new XamlDiagnostic(close.Span, "Expected '>' to close end tag."));
            _lex.Back();
            endRef = name.Span;
        }

        string nameText = TextOf(name.Span);

        int matchIndex = -1;
        int i = 0;
        foreach (var e in _open) // top first
        {
            if (TextOf(e.OpenNameSpan) == nameText)
            {
                matchIndex = i;
                break;
            }
            i++;
        }

        if (matchIndex < 0)
        {
            _diagnostics.Add(new XamlDiagnostic(name.Span, $"Unmatched end tag '{nameText}'."));
            return;
        }

        for (int j = 0; j < matchIndex; j++)
        {
            var unclosed = _open.Pop();
            _diagnostics.Add(new XamlDiagnostic(unclosed.OpenNameSpan, "Unclosed tag."));
            unclosed.Span = TextSpan.FromToStart(unclosed.Span, endTok.Span);
        }

        var matched = _open.Pop();
        matched.CloseNameSpan = name.Span;
        matched.Span = TextSpan.FromTo(matched.Span, endRef);
    }

    void FinalizeOpenElements(TextSpan eofSpan)
    {
        while (_open.Count > 0)
        {
            var unclosed = _open.Pop();
            _diagnostics.Add(new XamlDiagnostic(unclosed.OpenNameSpan, "Unclosed tag."));
            unclosed.Span = TextSpan.FromTo(unclosed.Span, eofSpan);
        }
    }

    string TextOf(TextSpan span) =>
        span.Length > 0 ? _source.Substring(span.Start, span.Length) : string.Empty;

    bool IsWhitespaceOnly(TextSpan span)
    {
        for (int i = span.Start; i < span.End; i++)
        {
            var c = _source[i];
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                return false;
        }
        return true;
    }
}
