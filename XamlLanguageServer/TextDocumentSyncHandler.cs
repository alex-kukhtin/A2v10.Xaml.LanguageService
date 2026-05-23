// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;


namespace XamlLanguageServer;

internal class TextDocumentSyncHandler(DocumentStore _store) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "vxaml");

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.vxaml"),
            // Full: on every change VS sends the entire document text;
            // we stash it in DocumentStore for other handlers to read.
            Change = TextDocumentSyncKind.Full,
        };

    public override Task<Unit> Handle(DidOpenTextDocumentParams r, CancellationToken ct)
    {
        _store.Set(r.TextDocument.Uri, r.TextDocument.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams r, CancellationToken ct)
    {
        // Full-sync: the single change carries the entire document text.
        var full = r.ContentChanges.LastOrDefault();
        if (full is not null)
            _store.Set(r.TextDocument.Uri, full.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams r, CancellationToken ct)
    {
        _store.Remove(r.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams r, CancellationToken ct) => Unit.Task;
}
