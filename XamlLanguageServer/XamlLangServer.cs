// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using OmniSharp.Extensions.LanguageServer.Server;

namespace XamlLanguageServer;

// In-process LSP server. Lives for the lifetime of the OOP service host process —
// when the host dies, the run-task, streams and singletons (AssemblyResolver,
// XamlContextProvider) go with it. No Dispose: LanguageServerProvider exposes no
// restart hook, so this object is created once per process and torn down only by
// process exit.
//
// ── VS 2026 client capabilities (probed 2026-05-30 by dumping ClientCapabilities
//    at `initialize`) ─────────────────────────────────────────────────────────
// Recorded so feature work targets the real client surface. Re-probe if VS changes.
//
// Completion (textDocument/completion) — most rich features are OFF:
//   SnippetSupport          = false → no standard `$1`/`$0` tab stops in insert text.
//   CommitCharactersSupport = false → no per-item commit characters. A shared set is
//                                     still possible via registration AllCommitCharacters
//                                     or CompletionList.ItemDefaults.commitCharacters.
//   ContextSupport          = false → the request carries NO CompletionContext, so the
//                                     trigger character is NOT available; dispatch purely
//                                     off XamlDocument.ContextAt position classification.
//   InsertReplaceSupport    = false → no InsertReplaceEdit; use a plain single-range
//                                     TextEdit for prefixed names (x:Name, my:Button).
//   PreselectSupport        = false → cannot preselect an item.
//   ResolveSupport / ResolveAdditionalTextEditsSupport = false → no lazy resolve;
//                                     any AdditionalTextEdits must be sent upfront.
//   CompletionList.ItemDefaults honors: commitCharacters, editRange, insertTextFormat.
//   CompletionItemKind.ValueSet = full 1..25 (+ VS-proprietary 118115.. icon kinds).
//
// OnTypeFormatting: supported — already proven working (caret follows TextEdit shape;
//   see project memory project_ontypeformatting_caret).
//
// Hover: ContentFormat = ["plaintext"] ONLY → no Markdown; hover content must be plain
//   text (matters for the future HoverService).
//
// PublishDiagnostics: supported, but RelatedInformation / VersionSupport /
//   CodeDescriptionSupport / DataSupport all false → markers are span+message only.
//   Pull diagnostics also available (textDocument/diagnostic; _vs_supportsDiagnosticRequests).
//
// Supported, for later phases: SignatureHelp (ContextSupport=true), Definition /
//   TypeDefinition / Implementation (LinkSupport=false → return Location, not
//   LocationLink), References, DocumentHighlight, DocumentSymbol (flat only —
//   Hierarchical=false), Rename (PrepareSupport=true), FoldingRange,
//   LinkedEditingRange (← live open/close tag-pair editing), CodeAction (Data+Resolve),
//   SemanticTokens (full+range+delta), InlayHint, DocumentLink.
//
// VS-proprietary extensions (_vs_supportsVisualStudioExtensions = true):
//   _vs_onAutoInsert (dynamicRegistration=true) → VS's purpose-built on-type auto-insert
//     (closing tags, `=""`, snippet-on-type). Accepts VS snippet replies (with `$0`)
//     even though standard SnippetSupport=false. Candidate alternative to onTypeFormatting
//     for `/` `>` `=` if we ever need caret control beyond the (span,newText) shape —
//     needs the _vs_ protocol, NOT wired in OmniSharp out of the box.
//   _vs_supportedSnippetVersion = 1; _vs_supportsIconExtensions = true;
//   _vs_supportsDiagnosticRequests = true.
// ──────────────────────────────────────────────────────────────────────────────
public sealed class XamlLangServer
{
    private readonly AssemblyResolver _assemblyResolver = new();

    public XamlLangServer()
    {
    }

    // Starts the run loop. The caller must NOT await the returned Task:
    // LanguageServer.From inside waits for "initialize" from VS, which only
    // arrives after the provider returns the pipe — awaiting here would deadlock.
    // The caller MUST keep a reference to the returned Task (or to this server) in a
    // long-lived field so the async state machine is not GC-collected mid-flight.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "Returns the long-running background task; the method itself is not an async TAP method")]
    public Task Start(Stream input, Stream output) => RunAsync(input, output);

    private async Task RunAsync(Stream input, Stream output)
    {
        var server = await LanguageServer.From(opts => opts
            .WithInput(input)
            .WithOutput(output)
            .WithServices(s =>
            {
                s.AddSingleton<DocumentStore>()
                 .AddSingleton(_assemblyResolver)
                 .AddSingleton<XamlContextProvider>();
            })
            .OnInitialize((server, request, ct) =>
            {
                // VS 2026 advertises semanticTokens.dynamicRegistration = true but does
                // NOT actually honour a dynamic (client/registerCapability) registration
                // of the semantic-tokens provider — so the SemanticTokensHandler is never
                // invoked. Clearing the client flag forces OmniSharp to declare
                // semanticTokensProvider STATICALLY in the initialize response, which VS
                // does honour. Completion / onTypeFormatting are unaffected (VS honours
                // their dynamic registration). Probed against the working reference server.
                var semanticTokens = request.Capabilities?.TextDocument?.SemanticTokens;
                if (semanticTokens is { IsSupported: true, Value: { } cap })
                    cap.DynamicRegistration = false;
                return Task.CompletedTask;
            })
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<CompletionHandler>()
            .WithHandler<OnTypeFormattingHandler>()
            .WithHandler<SemanticTokensHandler>());

        await server.WaitForExit;
        _assemblyResolver.Dispose();
    }
}
