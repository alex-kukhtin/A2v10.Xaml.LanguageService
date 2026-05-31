// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using XamlTolerantParser;

namespace XamlLanguageServer;

// On-type auto-edits: '>' inserts the matching close tag, '/' completes a
// self-closing tag, and the opening characters '"' '\'' '`' '{' auto-insert their
// closing counterpart (dumb pair-completion, fires anywhere). The position logic
// lives in XamlDocument.EditAt; this handler
// only adapts LSP <-> the parser's neutral XamlTypingEdit. The caret is implied by
// the edit shape — VS 2026 honours it (see XamlLangServer capability notes).
internal class OnTypeFormattingHandler(DocumentStore _store) : IDocumentOnTypeFormattingHandler
{
    public Task<TextEditContainer?> Handle(
        DocumentOnTypeFormattingParams request, CancellationToken ct)
    {
        var doc = _store.Get(request.TextDocument.Uri);
        TextEditContainer? result = null;

        if (doc != null && request.Character.Length == 1)
        {
            var edit = doc.EditAt(request.Position.Line, request.Position.Character, request.Character[0]);
            if (edit != null)
                result = new TextEditContainer(new TextEdit
                {
                    Range = ToRange(edit.Replace),
                    NewText = edit.NewText,
                });
        }

        return Task.FromResult(result);
    }

    public DocumentOnTypeFormattingRegistrationOptions GetRegistrationOptions(
        DocumentOnTypeFormattingCapability capability,
        ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.vxaml"),
            FirstTriggerCharacter = ">",
            MoreTriggerCharacter = new Container<string>("/", "\"", "'", "`", "{"),
        };

    private static Range ToRange(TextSpan span) =>
        new(span.StartLine, span.StartColumn, span.EndLine, span.EndColumn);
}
