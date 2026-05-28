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
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<CompletionHandler>());

        await server.WaitForExit;
        _assemblyResolver.Dispose();
    }
}
