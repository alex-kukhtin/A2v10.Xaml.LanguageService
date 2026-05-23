using System;

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

    int _pos;
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

        int start = _pos;

        if (_ch == '<')
            return ScanLessThan();

        while (_ch != NULL_CHAR && _ch != '<')
            NextChar();

        return new Token(TokenKind.Content, SpanFrom(start));
    }

    Token ScanLessThan()
    {
        int start = _pos;
        NextChar(); // consume '<'

        // <!-- comment -->
        if (_ch == '!' && Peek(1) == '-' && Peek(2) == '-')
        {
            NextChar(); NextChar(); NextChar(); // consume '!--'
            return ScanCommentBody(start);
        }

        // <![CDATA[ ... ]]>
        if (_ch == '!' && Peek(1) == '[' && Peek(2) == 'C' && Peek(3) == 'D'
            && Peek(4) == 'A' && Peek(5) == 'T' && Peek(6) == 'A' && Peek(7) == '[')
        {
            for (int i = 0; i < 8; i++) NextChar(); // consume '![CDATA['
            return ScanCDataBody(start);
        }

        // </
        if (_ch == '/')
        {
            NextChar();
            _state = LexerState.WithinTag;
            _elementNameEmittedInCurrentTag = false;
            return new Token(TokenKind.EndTagOpen, SpanFrom(start));
        }

        // <
        _state = LexerState.WithinTag;
        _elementNameEmittedInCurrentTag = false;
        return new Token(TokenKind.StartTagOpen, SpanFrom(start));
    }

    Token ScanCommentBody(int startIncludingOpening)
    {
        // Look for "-->", otherwise consume to EOF (IsUnterminated).
        while (_ch != NULL_CHAR)
        {
            if (_ch == '-' && Peek(1) == '-' && Peek(2) == '>')
            {
                NextChar(); NextChar(); NextChar(); // consume '-->'
                _state = LexerState.WithinContent;
                return new Token(TokenKind.Comment, SpanFrom(startIncludingOpening));
            }
            NextChar();
        }

        _state = LexerState.WithinContent;
        return new Token(TokenKind.Comment, SpanFrom(startIncludingOpening), IsUnterminated: true);
    }

    Token ScanCDataBody(int startIncludingOpening)
    {
        while (_ch != NULL_CHAR)
        {
            if (_ch == ']' && Peek(1) == ']' && Peek(2) == '>')
            {
                NextChar(); NextChar(); NextChar(); // consume ']]>'
                _state = LexerState.WithinContent;
                return new Token(TokenKind.CData, SpanFrom(startIncludingOpening));
            }
            NextChar();
        }

        _state = LexerState.WithinContent;
        return new Token(TokenKind.CData, SpanFrom(startIncludingOpening), IsUnterminated: true);
    }

    Token ScanInTag()
    {
        SkipWhitespace();

        if (_ch == NULL_CHAR)
            return Eof();

        int start = _pos;

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
            return new Token(TokenKind.TagClose, SpanFrom(start));
        }

        if (_ch == '/' && Peek(1) == '>')
        {
            NextChar(); NextChar();
            _state = LexerState.WithinContent;
            return new Token(TokenKind.SelfTagClose, SpanFrom(start));
        }

        if (IsNameStartChar(_ch))
        {
            // First name in a tag is the element name; subsequent names are attribute names.
            // The lexer distinguishes them by whether an ElementName has been emitted in
            // the current tag. Track via a small piece of state on the lexer.
            return ScanNameInTag(start);
        }

        // Unrecognized character inside a tag — emit Unknown and stay in WithinTag
        // so the parser can re-synchronize. The parser drives recovery; the lexer only reports.
        NextChar();
        return new Token(TokenKind.Unknown, SpanFrom(start));
    }

    bool _elementNameEmittedInCurrentTag;

    Token ScanNameInTag(int start)
    {
        while (IsNameChar(_ch))
            NextChar();

        var span = SpanFrom(start);

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

        int start = _pos;

        if (_ch == '=')
        {
            NextChar();
            _state = LexerState.BeforeAttributeValue;
            return new Token(TokenKind.Equals, SpanFrom(start));
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

        int start = _pos;

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
                return new Token(TokenKind.AttributeValue, SpanFrom(start));
            }

            // EOF without closing quote.
            _state = LexerState.WithinTag;
            return new Token(TokenKind.AttributeValue, SpanFrom(start), IsUnterminated: true);
        }

        // Value without quotes — not standard XML but tolerate: emit as Unknown and recover.
        _state = LexerState.WithinTag;
        return new Token(TokenKind.Unknown, SpanFrom(start));
    }

    // Slot for future heuristics. Right now: matching quote only.
    bool IsStringTerminator(char ch, char openingQuote)
    {
        return ch == openingQuote;
    }

    Token Eof()
    {
        _state = LexerState.WithinContent;
        return new Token(TokenKind.EndOfFile, new TextSpan(_pos, 0));
    }

    TextSpan SpanFrom(int start) => new(start, _pos - start);

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
            _pos++;
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
