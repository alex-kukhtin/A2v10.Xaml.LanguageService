// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using XamlTolerantParser;

namespace XamlLanguageServer;

internal class CompletionHandler(
    DocumentStore _store,
    XamlContextProvider _provider) : ICompletionHandler
{
    // Reference set of CompletionItemKind values — kept for future use when
    // attribute / value / extension completions land. Not currently returned
    // to the client.
    private static readonly CompletionList Items = new(
        [
            new CompletionItem { Label = "TagText",          Kind = CompletionItemKind.Text,          Detail = "Xaml element" },
            new CompletionItem { Label = "TagMethod",        Kind = CompletionItemKind.Method,        Detail = "Xaml element" },
            new CompletionItem { Label = "TagFunction",      Kind = CompletionItemKind.Function,      Detail = "Xaml element" },
            new CompletionItem { Label = "TagConstructor",   Kind = CompletionItemKind.Constructor,   Detail = "Xaml element" },
            new CompletionItem { Label = "TagField",         Kind = CompletionItemKind.Field,         Detail = "Xaml element" },
            new CompletionItem { Label = "TagVariable",      Kind = CompletionItemKind.Variable,      Detail = "Xaml element" },
            new CompletionItem { Label = "TagClass",         Kind = CompletionItemKind.Class,         Detail = "Xaml element" },
            new CompletionItem { Label = "TagInterface",     Kind = CompletionItemKind.Interface,     Detail = "Xaml element" },
            new CompletionItem { Label = "TagModule",        Kind = CompletionItemKind.Module,        Detail = "Xaml element" },
            new CompletionItem { Label = "TagProperty",      Kind = CompletionItemKind.Property,      Detail = "Xaml element" },
            new CompletionItem { Label = "TagUnit",          Kind = CompletionItemKind.Unit,          Detail = "Xaml element" },
            new CompletionItem { Label = "TagValue",         Kind = CompletionItemKind.Value,         Detail = "Xaml element" },
            new CompletionItem { Label = "TagEnum",          Kind = CompletionItemKind.Enum,          Detail = "Xaml element" },
            new CompletionItem { Label = "TagKeyword",       Kind = CompletionItemKind.Keyword,       Detail = "Xaml element" },
            new CompletionItem { Label = "TagSnippet",       Kind = CompletionItemKind.Snippet,       Detail = "Xaml element" },
            new CompletionItem { Label = "TagColor",         Kind = CompletionItemKind.Color,         Detail = "Xaml element" },
            new CompletionItem { Label = "TagFile",          Kind = CompletionItemKind.File,          Detail = "Xaml element" },
            new CompletionItem { Label = "TagReference",     Kind = CompletionItemKind.Reference,     Detail = "Xaml element" },
            new CompletionItem { Label = "TagFolder",        Kind = CompletionItemKind.Folder,        Detail = "Xaml element" },
            new CompletionItem { Label = "TagEnumMember",    Kind = CompletionItemKind.EnumMember,    Detail = "Xaml element" },
            new CompletionItem { Label = "TagConstant",      Kind = CompletionItemKind.Constant,      Detail = "Xaml element" },
            new CompletionItem { Label = "TagStruct",        Kind = CompletionItemKind.Struct,        Detail = "Xaml element" },
            new CompletionItem { Label = "TagEvent",         Kind = CompletionItemKind.Event,         Detail = "Xaml element" },
            new CompletionItem { Label = "TagOperator",      Kind = CompletionItemKind.Operator,      Detail = "Xaml element" },
            new CompletionItem { Label = "TagTypeParameter", Kind = CompletionItemKind.TypeParameter, Detail = "Xaml element" },
        ]
    );

    public Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var doc = _store.Get(request.TextDocument.Uri);
        if (doc == null)
            return Task.FromResult(new CompletionList());

        var items = _provider.Complete(doc, request.Position.Line, request.Position.Character)
            .Select(e => new CompletionItem
            {
                Label = e.Label,
                Kind = MapKind(e.Kind),
                Detail = e.Detail,
            })
            .ToArray();
        return Task.FromResult(new CompletionList(items));
    }

    private static CompletionItemKind MapKind(XamlCompletionKind kind) => kind switch
    {
        XamlCompletionKind.Tag => CompletionItemKind.Class,
        XamlCompletionKind.Attribute => CompletionItemKind.Property,
        XamlCompletionKind.EnumValue => CompletionItemKind.EnumMember,
        _ => CompletionItemKind.Text,
    };

    public CompletionRegistrationOptions GetRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.vxaml"),
            TriggerCharacters = new Container<String>(["<"]),
            ResolveProvider = false,
        };
}
