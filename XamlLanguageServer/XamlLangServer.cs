// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using OmniSharp.Extensions.LanguageServer.Server;

namespace XamlLanguageServer;

public sealed class XamlLangServer(Func<CancellationToken, Task<String?>> findCsproj, CancellationToken externalCt) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
    private readonly AssemblyResolver _assemblyResolver = new(findCsproj);

    private LanguageServer? _server;
    private Task? _runTask;
    private Int32 _disposed;

    // Starts the run loop fire-and-forget. The Task is kept so Dispose can await exit.
    // Awaiting LanguageServer.From here would deadlock: it waits for initialize from VS,
    // which only arrives after the provider returns the pipe.
    public Task StartAsync(Stream input, Stream output)
    {
        _runTask = RunAsync(input, output);
        return _runTask;
    }

    private async Task RunAsync(Stream input, Stream output)
    {
        _server = await LanguageServer.From(opts => opts
            .WithInput(input)
            .WithOutput(output)
            .WithServices(s =>
            {
                s.AddSingleton<DocumentStore>()
                 .AddSingleton(_assemblyResolver);
            })
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<CompletionHandler>(), _cts.Token);

        await _server.WaitForExit;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { await _cts.CancelAsync(); } catch { /* CTS already disposed — ignore */ }

        _server?.Dispose();

        if (_runTask is not null)
        {
            // Wait for graceful exit, but with a bounded timeout — never block forever.
            try
            {
                await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
            catch
            {
                // Any error from _runTask is already handled by the provider's ContinueWith subscriber.
            }
        }

        _assemblyResolver.Dispose();
        _cts.Dispose();
    }
}
