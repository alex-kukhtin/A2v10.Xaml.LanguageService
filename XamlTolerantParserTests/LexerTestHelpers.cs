using System.Collections.Generic;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

internal static class LexerTestHelpers
{
    public static List<Token> TokenizeAll(string source)
    {
        var lexer = new XamlLexer(source);
        var tokens = new List<Token>();
        while (true)
        {
            var tok = lexer.NextToken();
            tokens.Add(tok);
            if (tok.Kind == TokenKind.EndOfFile)
                return tokens;
        }
    }

    public static string TextOf(this Token t, string source) =>
        source.Substring(t.Span.Start, t.Span.Length);
}
