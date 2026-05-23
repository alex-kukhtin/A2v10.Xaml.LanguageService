// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace XamlServiceExtension;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
                id: "XamlLSP.49ea32ef-9f27-4462-980e-3b9609770226",
                version: ExtensionAssemblyVersion,
                publisherName: "Oleksandr Kukhtin",
                displayName: "A2v10 Xaml Language Service",
                description: "Extension description"),
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<ExtensionLog>();
    }
}
