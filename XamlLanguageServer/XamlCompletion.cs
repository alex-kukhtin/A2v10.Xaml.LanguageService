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
    Snippet,
}

// One completion suggestion. Already labelled and categorised by the provider;
// the handler turns each entry into a CompletionItem 1:1. Label is both the
// display text and (by default) the inserted text; InsertText overrides the
// latter when they must differ — e.g. a comment shows `!--` but inserts the
// whole `!--  -->` body.
internal sealed record XamlCompletionEntry(
    String Label, XamlCompletionKind Kind, String? Detail, String? InsertText = null);

// The result of a completion request: the suggestions plus the single source
// range they all replace — the token under the caret (zero-length for a pure
// insertion). One range per response, not per entry: the replaced token is a
// property of the position. The handler stamps EditSpan onto every item's
// TextEdit so the editor never computes the range from its own word heuristic
// (which eats the quotes in `Disabled="|"` and splits prefixes on ':').
//
// ValueQuote is the quote character of an attribute-value slot (`"` / `'`), or
// null when the position is not a value slot. It is a pure structural fact — the
// provider states "this slot is delimited by quote X", nothing about clients.
// The handler decides whether to append a matching closing quote, keyed on the
// trigger character: when the completion session was opened by TYPING the quote
// (`"`-trigger), that session races onTypeFormatting's auto-pair and VS eats the
// closing quote on commit, so the handler appends it back — yielding a balanced
// `Background="White"`. A Ctrl+Space session (no trigger char) opens against the
// already-settled `=""` buffer and is left untouched (appending there would
// double the quote). See CLAUDE.md "Open issues" — the quote-race.
internal sealed record XamlCompletionResult(
    IReadOnlyList<XamlCompletionEntry> Entries, TextSpan EditSpan, Char? ValueQuote = null);
