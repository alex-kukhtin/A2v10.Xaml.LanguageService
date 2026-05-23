// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipelines;

using Microsoft.VisualStudio.Extensibility;

using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;

using Nerdbank.Streams;

using Microsoft.VisualStudio.ProjectSystem.Query;

using XamlLanguageServer;


namespace XamlServiceExtension;

[VisualStudioContribution]
internal class XamlLanguageServerProvider(ExtensionLog _log) : LanguageServerProvider
{
    // The current server instance. If VS calls CreateServerConnectionAsync again
    // (e.g. when restarting the LSP), the previous instance must be disposed cleanly.
    private XamlLangServer? _currentServer;

    [VisualStudioContribution]
    public static DocumentTypeConfiguration VXamlDocumentType => new("vxaml")
    {
        FileExtensions = [".vxaml"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%XamlLSP.DisplayName%",
        [DocumentFilter.FromDocumentType(VXamlDocumentType)]);

    public override async Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var ws = Extensibility.Workspaces();

        async Task<String?> FindCsProjAsync(CancellationToken ct)
        {
            var results = await ws.QueryProjectsAsync(p => p.With(p => p.Path), ct);
            return results.FirstOrDefault()?.Path;
        }

        // If VS is restarting the server, tear down the previous instance before starting a new one.
        if (_currentServer is not null)
        {
            _log.Log("CreateServerConnectionAsync: disposing previous server instance");
            await _currentServer.DisposeAsync();
            _currentServer = null;
        }

        _log.Log("CreateServerConnectionAsync — in-process OmniSharp LSP");
        try
        {
            // In-memory duplex channel: one half goes to VS, the other to the server.
            var (sdkSide, serverSide) = FullDuplexStream.CreatePair();

            var server = new XamlLangServer(FindCsProjAsync, cancellationToken);
            // Fire-and-forget start: LanguageServer.From waits for initialize from VS,
            // which only arrives after we return the pipe.
            var runTask = server.StartAsync(serverSide, serverSide);
            _currentServer = server;

            _ = runTask.ContinueWith(
                t => _log.Log($"server faulted: {t.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            _log.Log("OmniSharp server is starting, returning pipe to VS");
            return sdkSide.UsePipe(0, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Log($"ABORT: {ex}");
            if (_currentServer is not null)
            {
                await _currentServer.DisposeAsync();
                _currentServer = null;
            }
            return null;
        }
    }

    public override Task OnServerInitializationResultAsync(
        ServerInitializationResult result,
        LanguageServerInitializationFailureInfo? failureInfo,
        CancellationToken cancellationToken)
    {
        _log.Log($"OnServerInitializationResult: {result}");

        if (failureInfo is not null)
            _log.Log($"  failureInfo: {failureInfo}");

        if (result == ServerInitializationResult.Failed)
            Enabled = false;

        return base.OnServerInitializationResultAsync(result, failureInfo, cancellationToken);
    }
}
