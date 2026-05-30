// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;

namespace XamlLanguageServer;

// A XamlType paired with the xmlns prefix it is bound to in a specific
// document. The XamlType itself is document-independent (cached server-lifetime
// in XamlAssembly); the prefix is the per-document binding from ClrPrefixes.
// This is the unit the type enumerator yields — completion-entry builders take
// it and apply their own role filter, kind, and label rules. A natural home for
// further per-occurrence helpers as they are needed.
internal sealed record FullXamlType(XamlType Type, String Prefix)
{
    // Tag / extension label as it appears in source: prefix-qualified unless
    // the binding is the default (empty) prefix.
    public String QualifiedName =>
        Prefix.Length == 0 ? Type.Name : $"{Prefix}:{Type.Name}";
}
