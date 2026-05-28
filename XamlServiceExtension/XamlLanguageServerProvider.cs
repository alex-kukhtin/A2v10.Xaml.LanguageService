// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipelines;

using Microsoft.VisualStudio.Extensibility;

using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;

using Nerdbank.Streams;

using XamlLanguageServer;


namespace XamlServiceExtension;

[VisualStudioContribution]
internal class XamlLanguageServerProvider(ExtensionLog _log) : LanguageServerProvider
{
    // Pin: VS holds the provider for the whole extension lifetime. Holding the run-task
    // here keeps the async state machine alive, which transitively keeps XamlLangServer
    // and AssemblyResolver reachable. Pinning the server object instead would NOT pin
    // the task — there's no field on XamlLangServer that references it.
    private Task? _runTask;

    [VisualStudioContribution]
    public static DocumentTypeConfiguration VXamlDocumentType => new("vxaml")
    {
        FileExtensions = [".vxaml"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%XamlLSP.DisplayName%",
        [DocumentFilter.FromDocumentType(VXamlDocumentType)]);

    // The CancellationToken parameter is the operation token (scoped to this call), not a
    // server-lifetime token — we deliberately do not propagate it into XamlLangServer.
    // The server is created once per provider lifetime and dies with the OOP host process.
    public override Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        _log.Log("CreateServerConnectionAsync — in-process OmniSharp LSP");

        // In-memory duplex channel: one half goes to VS, the other to the server.
        var (sdkSide, serverSide) = FullDuplexStream.CreatePair();

        var server = new XamlLangServer();

        // Do not await: LanguageServer.From inside waits for "initialize" from VS,
        // which only arrives after we return the pipe.
        _runTask = server.Start(serverSide, serverSide);

        _ = _runTask.ContinueWith(
            t => _log.Log($"server faulted: {t.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _log.Log("OmniSharp server is starting, returning pipe to VS");
        return Task.FromResult<IDuplexPipe?>(sdkSide.UsePipe(0, null, CancellationToken.None));
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
