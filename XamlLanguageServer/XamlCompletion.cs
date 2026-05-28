// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;

namespace XamlLanguageServer;

// Domain-neutral category for a completion entry. The handler maps each to
// the corresponding LSP CompletionItemKind without further reasoning.
internal enum XamlCompletionKind
{
    Tag,
    Attribute,
    EnumValue,
}

// One completion suggestion. Already labelled and categorised by the provider;
// the handler turns each entry into a CompletionItem 1:1.
internal sealed record XamlCompletionEntry(String Label, XamlCompletionKind Kind, String? Detail);
