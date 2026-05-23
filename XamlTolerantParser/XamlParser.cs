using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlParser
{
    readonly string _source;
    readonly XamlLexer _lex;
    readonly Stack<XamlElement> _open = new();
    readonly List<XamlNode> _roots = new();
    readonly List<XamlDiagnostic> _diagnostics = new();

    XamlParser(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lex = new XamlLexer(source);
    }

    public static XamlDocument Parse(string source) =>
        new XamlParser(source).ParseDocument();

    XamlDocument ParseDocument()
    {
        while (true)
        {
            var tok = _lex.NextToken();
            if (tok.Kind == TokenKind.EndOfFile)
                break;
            HandleToken(tok);
        }
        FinalizeOpenElements();
        return new XamlDocument(_roots, _diagnostics);
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
        var name = _lex.NextToken();
        if (name.Kind != TokenKind.ElementName)
        {
            _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Expected element name."));
            _lex.Back();
            return;
        }

        var elem = new XamlElement(name.Span)
        {
            Span = new TextSpan(openTok.Span.Start, name.Span.End - openTok.Span.Start),
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
                    elem.Span = new TextSpan(elem.Span.Start, tok.Span.End - elem.Span.Start);
                    _open.Push(elem);
                    return;

                case TokenKind.SelfTagClose:
                    elem.IsSelfClosing = true;
                    elem.Span = new TextSpan(elem.Span.Start, tok.Span.End - elem.Span.Start);
                    return;

                case TokenKind.EndOfFile:
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Unclosed tag."));
                    elem.Span = new TextSpan(elem.Span.Start, _source.Length - elem.Span.Start);
                    _lex.Back();
                    return;

                case TokenKind.StartTagOpen:
                    // The current tag never reached '>'. Close it as a sibling (no push to _open),
                    // then let the parser process the new tag.
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Tag is not closed."));
                    elem.Span = new TextSpan(elem.Span.Start, tok.Span.Start - elem.Span.Start);
                    ParseStartTag(tok);
                    return;

                case TokenKind.EndTagOpen:
                    // Same idea, but the next thing is somebody's close tag — let the document loop handle it.
                    _diagnostics.Add(new XamlDiagnostic(openTok.Span, "Tag is not closed."));
                    elem.Span = new TextSpan(elem.Span.Start, tok.Span.Start - elem.Span.Start);
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
            attr.Span = new TextSpan(nameTok.Span.Start, next.Span.End - nameTok.Span.Start);
            return;
        }

        var value = MakeValue(valTok);
        value.Parent = attr;
        attr.Value = value;
        attr.Span = new TextSpan(nameTok.Span.Start, valTok.Span.End - nameTok.Span.Start);
    }

    XamlValue MakeValue(Token valTok)
    {
        char quote = _source[valTok.Span.Start];
        int contentStart = valTok.Span.Start + 1;
        int contentEnd = valTok.IsUnterminated ? valTok.Span.End : valTok.Span.End - 1;
        var contentSpan = new TextSpan(contentStart, contentEnd - contentStart);
        return new XamlValue(valTok.Span, contentSpan, quote, valTok.IsUnterminated);
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
        int endOfCloseTag;
        if (close.Kind == TokenKind.TagClose)
        {
            endOfCloseTag = close.Span.End;
        }
        else
        {
            _diagnostics.Add(new XamlDiagnostic(close.Span, "Expected '>' to close end tag."));
            _lex.Back();
            endOfCloseTag = name.Span.End;
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
            unclosed.Span = new TextSpan(unclosed.Span.Start, endTok.Span.Start - unclosed.Span.Start);
        }

        var matched = _open.Pop();
        matched.CloseNameSpan = name.Span;
        matched.Span = new TextSpan(matched.Span.Start, endOfCloseTag - matched.Span.Start);
    }

    void FinalizeOpenElements()
    {
        while (_open.Count > 0)
        {
            var unclosed = _open.Pop();
            _diagnostics.Add(new XamlDiagnostic(unclosed.OpenNameSpan, "Unclosed tag."));
            unclosed.Span = new TextSpan(unclosed.Span.Start, _source.Length - unclosed.Span.Start);
        }
    }

    string TextOf(TextSpan span) =>
        span.Length > 0 ? _source.Substring(span.Start, span.Length) : string.Empty;
}
