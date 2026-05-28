// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Concurrent;

using OmniSharp.Extensions.LanguageServer.Protocol;

using XamlTolerantParser;

namespace XamlLanguageServer;

// Holds the latest parsed XamlDocument per open file. Parsing happens on Set
// (full reparse — the parser is non-incremental); other handlers read the
// cached tree via Get and never reparse themselves.
internal class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, XamlDocument> _map = new();

    public XamlDocument Set(DocumentUri uri, String? text)
    {
        var doc = XamlParser.Parse(text ?? String.Empty);
        _map[uri] = doc;
        return doc;
    }

    public void Remove(DocumentUri uri) => _map.TryRemove(uri, out _);

    public XamlDocument? Get(DocumentUri uri) =>
        _map.TryGetValue(uri, out var d) ? d : null;
}
