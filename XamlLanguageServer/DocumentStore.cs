// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Concurrent;

using OmniSharp.Extensions.LanguageServer.Protocol;

namespace XamlLanguageServer;

internal class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, String> _map = new();

    public void Set(DocumentUri uri, String? text) => _map[uri] = text ?? String.Empty;

    public void Remove(DocumentUri uri) => _map.TryRemove(uri, out _);

    public String Get(DocumentUri uri) => _map.TryGetValue(uri, out var t) ? t : String.Empty;
}
