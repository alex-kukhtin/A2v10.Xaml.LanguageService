// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using XamlTolerantParser;

namespace XamlLanguageServer;

// Syntax colouring via LSP semantic tokens (the replacement for the TextMate grammar).
// VS maps each emitted token type to a theme colour; the default themes collapse the ~23
// standard types into only a handful of distinct colours, so we deliberately use just
// THREE meaningful ones (eyeballed live against the VS dark theme), chosen to echo the
// familiar standard-XML palette:
//   String  (red)   — element tag names and markup-extension type names
//   Type    (teal)  — attribute names, extension argument names, positional arguments
//   Keyword (blue)  — attribute values, extension argument values
// plus Comment (green) for comments AND CDATA (both read as inert text). A user who wants
// a different palette remaps these
// classifications in Tools -> Options -> Fonts and Colors (we pick semantically sensible
// types, the theme owns the colour).
//
// Source of truth is the cached XamlDocument tree (DocumentStore), so a semantic-tokens
// request adds NO extra parse — only a tree walk.
//
// Scope (decided): colour ONLY the spans the parse tree exposes directly — tag/attribute/
// extension names, values, comments, CDATA. Punctuation ('<' '>' '/' '=' '"' '{' '}' ',')
// and the xmlns prefix of a qualified name (my:Button) are NOT separately coloured: they
// have no dedicated span and would require manual arithmetic. A qualified name is coloured
// whole, prefix included.
//
// VS 2026 advertises SemanticTokens full+range+delta (see XamlLangServer capability
// snapshot). We register full only — Delta=false, Range=false: vxaml files are small,
// the transport is the in-process duplex stream, and a full token list per change is
// cheap. Revisit delta/range only if a measurement says otherwise.
internal class SemanticTokensHandler(DocumentStore _store) : SemanticTokensHandlerBase
{
    // A colourable span paired with the token type it maps to. Produced by the pure
    // Tokens() mapping (unit-testable without the LSP builder), consumed by Tokenize.
    internal readonly record struct ColorToken(TextSpan Span, SemanticTokenType Type);

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.vxaml"),
            // Echo back the client's own advertised legend instead of hardcoding one:
            // VS lists every token type/modifier it understands, and we index into that
            // exact list when pushing — no risk of naming a type the client rejects.
            // capability may be null: OmniSharp also calls this when folding the handler
            // into the STATIC server capabilities (the path VS actually uses — dynamic
            // registration of semantic tokens is NOT honoured by VS, see XamlLangServer
            // OnInitialize), where there is no client capability to echo. Guard the null.
            Legend = new SemanticTokensLegend
            {
                TokenTypes = capability?.TokenTypes ?? new Container<SemanticTokenType>(),
                TokenModifiers = capability?.TokenModifiers ?? new Container<SemanticTokenModifier>(),
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false,
        };

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params, CancellationToken cancellationToken) =>
        Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    protected override Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        var doc = _store.Get(identifier.TextDocument.Uri);
        if (doc == null)
            return Task.CompletedTask;

        foreach (var token in Tokens(doc))
            PushSpan(builder, doc, token.Span, token.Type);

        return Task.CompletedTask;
    }

    // Maps the parse tree to a flat, ascending-by-position list of colour tokens.
    // Pure (no builder, no LSP) so the node -> token-type decisions are unit-testable.
    //
    // The list is SORTED by start position because the builder encodes each token as a
    // delta from the previous one and so requires ascending order — but the tree's
    // document order does NOT give that: an element's CloseNameSpan (`</a>`) sits AFTER
    // all of its children, yet Visit emits it during the element's own (pre-children)
    // visit. Collect-then-sort sidesteps every such ordering subtlety.
    internal static List<ColorToken> Tokens(XamlDocument doc)
    {
        var tokens = new List<ColorToken>();

        doc.Visit(node =>
        {
            switch (node)
            {
                case XamlElement e:
                    tokens.Add(new ColorToken(e.OpenNameSpan, SemanticTokenType.String));
                    if (e.CloseNameSpan is { } close)
                        tokens.Add(new ColorToken(close, SemanticTokenType.String));
                    break;
                case XamlAttribute a:
                    tokens.Add(new ColorToken(a.NameSpan, SemanticTokenType.Type));
                    break;
                // A value that hosts a markup extension is coloured by the extension's
                // own nodes (type name / arg names / arg values); only a plain string
                // value is coloured as a whole here.
                case XamlValue v when v.Extension is null:
                    tokens.Add(new ColorToken(v.ContentSpan, SemanticTokenType.Keyword));
                    break;
                case XamlComment c:
                    tokens.Add(new ColorToken(c.Span, SemanticTokenType.Comment));
                    break;
                case XamlCData cd:
                    // CDATA shares the comment colour — it reads as inert text, like a comment.
                    tokens.Add(new ColorToken(cd.Span, SemanticTokenType.Comment));
                    break;
                case XamlExtension x:
                    tokens.Add(new ColorToken(x.TypeNameSpan, SemanticTokenType.String));
                    break;
                // A named argument: name like a property (Type), its bare value like a
                // value (Keyword). A nested-extension value is NOT handled here — it is
                // coloured by the XamlExtension case when the walk descends into it.
                case XamlNamedArgument na:
                    tokens.Add(new ColorToken(na.NameSpan, SemanticTokenType.Type));
                    if (na.Value is XamlExtensionStringValue nsv)
                        tokens.Add(new ColorToken(nsv.Span, SemanticTokenType.Keyword));
                    break;
                // A positional argument (e.g. the key in {StaticResource Foo}) is coloured
                // as Type — same as a property name, distinct from a named value's Keyword.
                // The string value is coloured HERE (via its parent) rather than in a
                // standalone XamlExtensionStringValue case, because that node's colour
                // depends on whether its parent is a positional or a named argument.
                case XamlPositionalArgument pa:
                    if (pa.Value is XamlExtensionStringValue psv)
                        tokens.Add(new ColorToken(psv.Span, SemanticTokenType.Type));
                    break;
                // Not coloured: XamlText (markup text content — default foreground).
            }
        });

        tokens.Sort(static (a, b) =>
            a.Span.StartLine != b.Span.StartLine
                ? a.Span.StartLine - b.Span.StartLine
                : a.Span.StartColumn - b.Span.StartColumn);

        return tokens;
    }

    // Pushes a span as one or more tokens. The semantic-tokens protocol cannot encode a
    // token that crosses a line boundary, so a multi-line span (comment, CDATA, a wrapped
    // value) is split into one token per line: first line from its start column to the
    // line end, full middle lines, last line up to its end column. A trailing '\r' is
    // dropped from each line so the length matches the lexer's column counting (\r is not
    // counted as a column). Empty spans (e.g. an unnamed extension's zero-length type
    // name) produce nothing.
    private static void PushSpan(SemanticTokensBuilder builder, XamlDocument doc, TextSpan span, SemanticTokenType type)
    {
        if (span.Length <= 0)
            return;

        if (span.StartLine == span.EndLine)
        {
            builder.Push(span.StartLine, span.StartColumn, span.Length, type, NoModifiers);
            return;
        }

        var text = doc.TextOf(span);
        var line = span.StartLine;
        var col = span.StartColumn;
        var segStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                var segEnd = i;
                if (segEnd > segStart && text[segEnd - 1] == '\r')
                    segEnd--;
                var len = segEnd - segStart;
                if (len > 0)
                    builder.Push(line, col, len, type, NoModifiers);
                line++;
                col = 0;
                segStart = i + 1;
            }
        }
    }

    private static readonly SemanticTokenModifier[] NoModifiers = [];
}
