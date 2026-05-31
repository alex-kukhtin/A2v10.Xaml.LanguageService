// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;

using XamlTolerantParser;

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

// The result of a completion request: the suggestions plus the single source
// range they all replace — the token under the caret (zero-length for a pure
// insertion). One range per response, not per entry: the replaced token is a
// property of the position. The handler stamps EditSpan onto every item's
// TextEdit so the editor never computes the range from its own word heuristic
// (which eats the quotes in `Disabled="|"` and splits prefixes on ':').
internal sealed record XamlCompletionResult(IReadOnlyList<XamlCompletionEntry> Entries, TextSpan EditSpan);
