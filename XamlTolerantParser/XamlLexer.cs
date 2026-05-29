using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlLexer
{
    const char NULL_CHAR = '\0';

    enum LexerState
    {
        WithinContent,
        WithinTag,
        AfterAttributeName,
        BeforeAttributeValue,
    }

    readonly string _text;
    readonly int _len;
    // Offsets of the first character of each line. _lineStarts[0] == 0 always.
    // Pushed during NextChar() on every '\n' consumed. Used by XamlDocument.OffsetAt
    // to convert (line, column) → offset without scanning the whole source.
    readonly List<int> _lineStarts = new() { 0 };

    int _pos;
    int _line;
    int _column;
    char _ch;

    LexerState _state;

    bool _hasPushedBack;
    Token _pushedBack;

    public XamlLexer(string source)
    {
        _text = source ?? throw new ArgumentNullException(nameof(source));
        _len = _text.Length;
        _state = LexerState.WithinContent;
        ReadChar();
    }

    // Snapshot of line-start offsets collected during lexing. The parser
    // hands this off to XamlDocument after Parse completes.
    public int[] GetLineStarts() => _lineStarts.ToArray();

    public Token NextToken()
    {
        if (_hasPushedBack)
        {
            _hasPushedBack = false;
            return _pushedBack;
        }

        var token = ScanNext();
        _pushedBack = token;
        return token;
    }

    public void Back()
    {
        if (_hasPushedBack)
            throw new InvalidOperationException("Back() depth exceeded; only one push-back is supported.");
        _hasPushedBack = true;
    }

    Token ScanNext()
    {
        return _state switch
        {
            LexerState.WithinContent => ScanContent(),
            LexerState.WithinTag => ScanInTag(),
            LexerState.AfterAttributeName => ScanAfterAttributeName(),
            LexerState.BeforeAttributeValue => ScanBeforeAttributeValue(),
            _ => Eof(),
        };
    }

    Token ScanContent()
    {
        if (_ch == NULL_CHAR)
            return Eof();

        var (start, sLine, sCol) = Snapshot();

        if (_ch == '<')
            return ScanLessThan();

        while (_ch != NULL_CHAR && _ch != '<')
            NextChar();

        return new Token(TokenKind.Content, SpanFrom(start, sLine, sCol));
    }

    Token ScanLessThan()
    {
        var (start, sLine, sCol) = Snapshot();
        NextChar(); // consume '<'

        // <!-- comment -->
        if (_ch == '!' && Peek(1) == '-' && Peek(2) == '-')
        {
            NextChar(); NextChar(); NextChar(); // consume '!--'
            return ScanCommentBody(start, sLine, sCol);
        }

        // <![CDATA[ ... ]]>
        if (_ch == '!' && Peek(1) == '[' && Peek(2) == 'C' && Peek(3) == 'D'
            && Peek(4) == 'A' && Peek(5) == 'T' && Peek(6) == 'A' && Peek(7) == '[')
        {
            for (int i = 0; i < 8; i++) NextChar(); // consume '![CDATA['
            return ScanCDataBody(start, sLine, sCol);
        }

        // </
        if (_ch == '/')
        {
            NextChar();
            _state = LexerState.WithinTag;
            _elementNameEmittedInCurrentTag = false;
            return new Token(TokenKind.EndTagOpen, SpanFrom(start, sLine, sCol));
        }

        // <
        _state = LexerState.WithinTag;
        _elementNameEmittedInCurrentTag = false;
        return new Token(TokenKind.StartTagOpen, SpanFrom(start, sLine, sCol));
    }

    Token ScanCommentBody(int start, int sLine, int sCol)
    {
        // Look for "-->", otherwise consume to EOF (IsUnterminated).
        while (_ch != NULL_CHAR)
        {
            if (_ch == '-' && Peek(1) == '-' && Peek(2) == '>')
            {
                NextChar(); NextChar(); NextChar(); // consume '-->'
                _state = LexerState.WithinContent;
                return new Token(TokenKind.Comment, SpanFrom(start, sLine, sCol));
            }
            NextChar();
        }

        _state = LexerState.WithinContent;
        return new Token(TokenKind.Comment, SpanFrom(start, sLine, sCol), IsUnterminated: true);
    }

    Token ScanCDataBody(int start, int sLine, int sCol)
    {
        while (_ch != NULL_CHAR)
        {
            if (_ch == ']' && Peek(1) == ']' && Peek(2) == '>')
            {
                NextChar(); NextChar(); NextChar(); // consume ']]>'
                _state = LexerState.WithinContent;
                return new Token(TokenKind.CData, SpanFrom(start, sLine, sCol));
            }
            NextChar();
        }

        _state = LexerState.WithinContent;
        return new Token(TokenKind.CData, SpanFrom(start, sLine, sCol), IsUnterminated: true);
    }

    Token ScanInTag()
    {
        SkipWhitespace();

        if (_ch == NULL_CHAR)
            return Eof();

        var (start, sLine, sCol) = Snapshot();

        if (_ch == '<')
        {
            // '<' inside a tag means the current tag was never closed.
            // Bail to content state without consuming so the next token is StartTagOpen/EndTagOpen.
            _state = LexerState.WithinContent;
            return ScanContent();
        }

        if (_ch == '>')
        {
            NextChar();
            _state = LexerState.WithinContent;
            return new Token(TokenKind.TagClose, SpanFrom(start, sLine, sCol));
        }

        if (_ch == '/' && Peek(1) == '>')
        {
            NextChar(); NextChar();
            _state = LexerState.WithinContent;
            return new Token(TokenKind.SelfTagClose, SpanFrom(start, sLine, sCol));
        }

        if (IsNameStartChar(_ch))
        {
            // First name in a tag is the element name; subsequent names are attribute names.
            // The lexer distinguishes them by whether an ElementName has been emitted in
            // the current tag. Track via a small piece of state on the lexer.
            return ScanNameInTag(start, sLine, sCol);
        }

        // Unrecognized character inside a tag — emit Unknown and stay in WithinTag
        // so the parser can re-synchronize. The parser drives recovery; the lexer only reports.
        NextChar();
        return new Token(TokenKind.Unknown, SpanFrom(start, sLine, sCol));
    }

    bool _elementNameEmittedInCurrentTag;

    Token ScanNameInTag(int start, int sLine, int sCol)
    {
        while (IsNameChar(_ch))
            NextChar();

        var span = SpanFrom(start, sLine, sCol);

        if (!_elementNameEmittedInCurrentTag)
        {
            _elementNameEmittedInCurrentTag = true;
            return new Token(TokenKind.ElementName, span);
        }

        _state = LexerState.AfterAttributeName;
        return new Token(TokenKind.AttributeName, span);
    }

    Token ScanAfterAttributeName()
    {
        SkipWhitespace();

        if (_ch == NULL_CHAR)
            return Eof();

        var (start, sLine, sCol) = Snapshot();

        if (_ch == '=')
        {
            NextChar();
            _state = LexerState.BeforeAttributeValue;
            return new Token(TokenKind.Equals, SpanFrom(start, sLine, sCol));
        }

        // No '=' after attribute name — go back to scanning the tag (boolean-style attribute).
        _state = LexerState.WithinTag;
        return ScanInTag();
    }

    Token ScanBeforeAttributeValue()
    {
        SkipWhitespace();

        if (_ch == NULL_CHAR)
            return Eof();

        var (start, sLine, sCol) = Snapshot();

        if (_ch == '<')
        {
            // Attribute value missing — '<' starts the next tag; bail without consuming.
            _state = LexerState.WithinContent;
            return ScanContent();
        }

        if (_ch == '"' || _ch == '\'')
        {
            char quote = _ch;
            NextChar(); // consume opening quote

            while (_ch != NULL_CHAR && !IsStringTerminator(_ch, quote))
                NextChar();

            if (_ch == quote)
            {
                NextChar(); // consume closing quote
                _state = LexerState.WithinTag;
                return new Token(TokenKind.AttributeValue, SpanFrom(start, sLine, sCol));
            }

            // EOF without closing quote.
            _state = LexerState.WithinTag;
            return new Token(TokenKind.AttributeValue, SpanFrom(start, sLine, sCol), IsUnterminated: true);
        }

        // Value without quotes — not standard XML but tolerate: emit as Unknown and recover.
        _state = LexerState.WithinTag;
        return new Token(TokenKind.Unknown, SpanFrom(start, sLine, sCol));
    }

    // Slot for future heuristics. Right now: matching quote only.
    bool IsStringTerminator(char ch, char openingQuote)
    {
        return ch == openingQuote;
    }

    Token Eof()
    {
        _state = LexerState.WithinContent;
        return new Token(TokenKind.EndOfFile, new TextSpan(_pos, 0, _line, _column, _line, _column));
    }

    (int pos, int line, int col) Snapshot() => (_pos, _line, _column);

    TextSpan SpanFrom(int start, int startLine, int startColumn) =>
        new(start, _pos - start, startLine, startColumn, _line, _column);

    void SkipWhitespace()
    {
        while (_ch != NULL_CHAR && char.IsWhiteSpace(_ch))
            NextChar();
    }

    char Peek(int offset)
    {
        int idx = _pos + offset;
        return idx < _len ? _text[idx] : NULL_CHAR;
    }

    void NextChar()
    {
        if (_pos < _len)
        {
            var consumed = _text[_pos];
            _pos++;
            if (consumed == '\n')
            {
                _line++;
                _column = 0;
                _lineStarts.Add(_pos);
            }
            else if (consumed != '\r')
            {
                // \r is swallowed: contributes nothing to line/column.
                // CRLF is a single line break via \n; lone \r is treated as
                // a no-op (acceptable since vxaml files come from VS editors).
                _column++;
            }
        }
        ReadChar();
    }

    void ReadChar()
    {
        _ch = _pos < _len ? _text[_pos] : NULL_CHAR;
    }

    static bool IsNameStartChar(char c) =>
        char.IsLetter(c) || c == '_';

    static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
}
