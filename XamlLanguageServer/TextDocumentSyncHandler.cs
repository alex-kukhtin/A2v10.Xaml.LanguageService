// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

using XamlTolerantParser;

namespace XamlLanguageServer;

internal class TextDocumentSyncHandler(
    DocumentStore _store,
    AssemblyResolver _resolver,
    ILanguageServerFacade _server) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "vxaml");

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.vxaml"),
            // Full: on every change VS sends the entire document text;
            // we reparse and stash the tree in DocumentStore.
            Change = TextDocumentSyncKind.Full
        };

    public override Task<Unit> Handle(DidOpenTextDocumentParams r, CancellationToken ct)
    {
        _resolver.EnsureFolderFor(r.TextDocument.Uri.GetFileSystemPath());
        var doc = _store.Set(r.TextDocument.Uri, r.TextDocument.Text);
        PublishDiagnostics(r.TextDocument.Uri, doc);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams r, CancellationToken ct)
    {
        // Full-sync: the single change carries the entire document text.
        var full = r.ContentChanges.LastOrDefault();
        if (full is not null)
        {
            _resolver.EnsureFolderFor(r.TextDocument.Uri.GetFileSystemPath());
            var doc = _store.Set(r.TextDocument.Uri, full.Text);
            PublishDiagnostics(r.TextDocument.Uri, doc);
        }
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams r, CancellationToken ct)
    {
        _store.Remove(r.TextDocument.Uri);
        // Clear stale markers in the client.
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = r.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>(),
        });
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams r, CancellationToken ct) => Unit.Task;

    private void PublishDiagnostics(DocumentUri uri, XamlDocument doc)
    {
        var diagnostics = doc.Diagnostics
            .Select(d => new Diagnostic
            {
                Range = ToRange(d.Span),
                Severity = DiagnosticSeverity.Error,
                Source = "vxaml",
                Message = d.Message,
            })
            .ToArray();

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(diagnostics),
        });
    }

    private static Range ToRange(TextSpan span) =>
        new(span.StartLine, span.StartColumn, span.EndLine, span.EndColumn);
}
