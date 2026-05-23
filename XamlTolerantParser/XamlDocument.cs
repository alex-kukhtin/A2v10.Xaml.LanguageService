using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlDocument
{
    public IReadOnlyList<XamlNode> Roots { get; }
    public IReadOnlyList<XamlDiagnostic> Diagnostics { get; }

    public XamlDocument(IReadOnlyList<XamlNode> roots, IReadOnlyList<XamlDiagnostic> diagnostics)
    {
        Roots = roots;
        Diagnostics = diagnostics;
    }
}
