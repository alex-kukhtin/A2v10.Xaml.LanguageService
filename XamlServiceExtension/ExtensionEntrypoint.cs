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
            id: "XamlLSP.C0A64D94-06E3-4485-A09C-46734812477F",
            version: ExtensionAssemblyVersion,
            publisherName: "Oleksandr Kukhtin",
            displayName: "A2v10 Xaml Language Service",
            description: "IntelliSense and tooling for A2v10 XAML (.vxaml) — completion, diagnostics and hover powered by an in-process Language Server."
            )
        {
            Icon = @"Resources\a2_logo_32.png",
            PreviewImage = @"Resources\a2_logo_200.png",
            MoreInfo = "https://github.com/alex-kukhtin/A2v10.Xaml.LanguageService"
        },
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<ExtensionLog>();
    }
}
