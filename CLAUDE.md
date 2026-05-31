# A2v10 XAML Language Service — Architecture & Decisions

A Language Server for the `.vxaml` dialect (not `.xaml`, not `.axaml`) targeting Visual Studio 2026.
Goal: WPF-level IntelliSense — completion, diagnostics, hover, go-to-definition.

## Conventions

- **All code comments and log messages are in English.** No Russian. This applies to inline comments, XML doc comments, and string literals fed to `ExtensionLog` or any future logger. Identifiers stay in English regardless (default).

## Projects

| Project | Role |
|---|---|
| `XamlServiceExtension` | VSIX entry point. VS Extensibility SDK 17.14. Hosts the LSP server in-process. |
| `XamlLanguageServer` | LSP server library. OmniSharp. Handlers, resolvers, document store. |
| `XamlTolerantParser` | Lexer, parser, document model, xmlns extraction, `FindNodeAt`, `ContextAt`, `EditAt`. No VS / no LSP / no I/O dependencies. Pure CLR over source text. |
| `XamlTolerantParserTests` | xUnit tests for parser, xmlns extraction, find-node-at, position classification (`ContextAt`), on-type edits (`EditAt`). |

---

## Two-layer architecture (proven end-to-end)

### Layer 1 — Syntax coloring: TextMate grammar
- Declarative `.plist` grammar, auto-loaded by VS from `Starterkit\Extensions\` — no registration needed.
- Copied from VS built-in XAML grammar. Changed: `fileTypes` → `vxaml`, `scopeName` → `text.vxaml` (must be unique), `uuid`.
- Internal patterns/scopes unchanged — reuse XML grammar and XAML theme.
- Maps to generic VS classifications: `markup node`, `markup attribute`, `markup attribute value`, `operator`, `string`, `comment`.
- **Color palette: intentionally not customized.** The color chain is: scope (grammar) → `vs.tmTheme` → VS classification → Fonts & Colors. Editing `vs.tmTheme` is a VS installation file shared across all TextMate languages — bad delivery story. Users can remap colors in Tools → Options → Fonts and Colors.

### Layer 2 — Intelligence: LSP server (OmniSharp)
- Hosted **in-process** inside the extension. Transport: in-memory duplex channel (`Nerdbank.Streams.FullDuplexStream`).
- Framework: `OmniSharp.Extensions.LanguageServer`.
- Why in-process: in VS Extensibility (out-of-process model), `AppContext.BaseDirectory` points to the VS ServiceHub host — impossible to locate a sibling exe. In-process also enables F5 debugging of the server.
- `StartAsync` must be called **fire-and-forget** — `LanguageServer.From` waits for `initialize` from VS, which only arrives after the pipe is returned. Awaiting it causes a deadlock.
- **VS 2026 client capabilities are recorded** in a comment block above `XamlLangServer` (probed once by dumping `ClientCapabilities` at `initialize`). Facts that shape feature work: completion `SnippetSupport` / `CommitCharactersSupport` / `ContextSupport` / `InsertReplaceSupport` are all **false** (⇒ no trigger character in the request — dispatch off `ContextAt`; plain single-range `TextEdit`s only), hover is **plaintext-only**, push diagnostics are span+message only, and VS exposes the proprietary `_vs_onAutoInsert` and `LinkedEditingRange`. Re-probe if VS changes; see [memory: VS 2026 client capabilities](/.claude/memory/project_vs2026_client_capabilities.md).

---

## Rejected approaches (do not revisit without strong reason)

- **Semantic tokens as TextMate replacement** — pull model breaks coloring on malformed input (i.e. almost every keystroke); valid only as an *additional* layer over TextMate.
- **`ITextViewTaggerProvider` / `ClassificationTag`** — classic MEF API, unavailable in the new VS Extensibility model without a hybrid VSIX.
- **Inheriting `.vxaml` from XML document type** — `DocumentTypeConfiguration.BaseDocumentType` is singular; setting it to XML breaks LSP.

**Root cause of the three above:** the new VS Extensibility model is isolated from the classic in-process editor infrastructure. TextMate grammar is the bypass.

- **Incremental parsing / `TextDocumentSyncKind.Incremental`** — vxaml documents are 100-200 lines typically, 1000 lines exceptional. A full reparse of a 50 KB file by the hand-written lexer (zero allocations, all spans are structs over the same source) takes ~100 µs–1 ms. Per-keystroke that is ≤10 ms/sec of CPU on the worst file we'll ever see — well below the noise floor on a budget dominated by assembly reflection, JSON, and VS rendering. Going incremental costs Roslyn-tier infrastructure (red-green tree or equivalent) to make position-offset updates after a splice cheap; the implementation is ~2000 lines of subtle code for a win that won't be measurable. `TextDocumentSyncKind.Incremental` separately would save nothing: the transport is the in-process `Nerdbank.Streams.FullDuplexStream` (memory copy, no network), and applying the delta still allocates a fresh full-length string. The two are independent — and neither is justified at our sizes.

---

## Assembly discovery — `@assemblies` convention

User puts the DLLs that the vxaml dialect references (`A2v10.Xaml.dll` etc.) into a folder literally named **`@assemblies`** somewhere above their `.vxaml` files. The server walks up the directory tree from a vxaml path until it finds one. That folder is the type universe for those files. No `project.assets.json`, no `PackageReference`, no `csproj` lookup, no VS Extensibility workspace queries — **pure file-system convention**.

**Discovery flow** in `AssemblyResolver`:
1. On `didOpen`/`didChange`, `TextDocumentSyncHandler` calls `_resolver.EnsureFolderFor(uri.GetFileSystemPath())`.
2. Resolver checks its `_anchors` list — if the new path starts with any known anchor (the *directory containing* `@assemblies`), nothing to do.
3. Otherwise walks `Path.GetDirectoryName` up until a directory with an `@assemblies` subfolder is found. Anchor + folder remembered.
4. If no `@assemblies` exists in the tree, nothing is registered. Lookups will return null silently.

A typical session sees 1-3 walk-ups total. Anchors live for the server lifetime; the per-tree-of-files cost is one `Directory.Exists` per ancestor.

**Loading & lookup** uses `System.Reflection.Metadata` — a `MetadataReader` over a `PEReader` — with **no `MetadataLoadContext`, no reflection, and no `System.Type` ever materialised.** `AssemblyResolver.GetAssembly(name)` finds `{name}.dll` in a registered `@assemblies` folder, reads its **bytes** once (`File.ReadAllBytes`), and hands them to `new PEReader(ImmutableArray<byte>)`. Reading bytes (rather than memory-mapping the file) means the DLL is **never locked** — the user can rebuild `A2v10.Xaml.dll` without killing VS. `XamlAssembly` consumes the `MetadataReader` and extracts everything it needs into plain strings during construction, so the `PEReader` can be disposed the moment the constructor returns (eager, like the old index).

**Why metadata, not MLC:** a language service that compares everything **by simple name** never needs a live `Type` or type identity. And a name lives physically in *this* file's tables: a `TypeRef` row stores the referenced type's name + namespace + `AssemblyRef` *as strings*, readable without loading the assembly that defines the type. MLC's whole job — resolving those refs into materialised `Type`s — is the wrong tool when you only want the strings (and it always resolves, dragging in `mscorlib`/BCL and throwing on any absent dependency). `MetadataReader` reads `TypeDef`/`TypeRef`/`TypeSpec` and signature blobs directly. Consequences:
- **No runtime-directory fallback, no `mscorlib` probe** — the entire MLC bootstrap is gone, and `@assemblies` does **not** need the BCL or sibling base assemblies present.
- **No transitive base-class closure requirement.** The base-class walk follows `TypeDefinition.BaseType` while it stays inside this file; a base that is a `TypeReference` (sibling assembly) or generic `TypeSpecification` is read **by name** and ends the walk — enough to match a dialect role marker (`RootContainer`, `UIElement`, `Dictionary\`2`, …). A missing sibling **never throws** — the reader stops at the name it already has. (The old MLC index threw on the first unresolvable ancestor; that "completeness is a hard requirement" gap is closed.)
- **Interfaces** are read from each type's `InterfaceImplementation` rows (directly-declared, no transitive resolution). The transitive closure *across the base-class chain* is reconstructed cheaply by also reading the interfaces of each same-file base class — so a type that inherits a dialect interface through a base (`SheetGroupCell : SheetCellGroup : ISheetCell`) is still recognised. Filtered to dialect (`A2v10.*`) namespaces.
- **Content-property element type** is decoded from signature blobs via `SignatureDecoder` (`MetaSignature`): `Rows : TableRowCollection : List<TableRow>` yields `TableRow`, all from this file — no `List<T>` resolution.
- **Enum values** come from the enum's literal static `Field` rows — but only when the enum is defined in the *same* file (its base is a readable `System.Enum` ref). A cross-file enum's values stay `null` (we don't open the other file).

**Stale cache:** a built `XamlAssembly` is cached by name for the server lifetime and not rebuilt when the DLL changes. Acceptable: vxaml types come from a stable controlled package; restart VS to pick up a new DLL version. No `FileSystemWatcher` needed.

---

## Parser & document model

**Syntax layer (done).** `XamlTolerantParser`:
- Tolerant, full reparse on every change (non-incremental). `XamlParser.Parse(string) → XamlDocument`.
- Lexer is hand-written (`XamlLexer`) with one-token pushback (`Back()`).
- Node hierarchy: `XamlNode` → `XamlElement`, `XamlText`, `XamlComment`, `XamlCData`, `XamlAttribute`, `XamlValue`, plus the markup-extension subtree (see below).
  - `XamlAttribute` is **not** in `element.Children` — it lives in `element.Attributes`. Children are content nodes only.
  - `XamlValue` is owned by `XamlAttribute.Value`. Carries `Span` (whole value with quotes), `ContentSpan` (inside quotes), `Quote`, `IsUnterminated`, `Extension?`.
  - No `XamlMalformedNode` — malformed input is represented by diagnostics plus best-effort recovery into normal nodes, not by a distinct node type.
- **Markup extensions are parsed.** `XamlValue.Extension` is populated when the content of a quoted attribute value starts with `{` and isn't the `{}` literal-string escape (XAML spec).
  - Subtree: `XamlExtension` (carries `OpenBraceSpan`, `TypeNameSpan`, `CloseBraceSpan?`, `Arguments`, `IsClosed`) → `XamlExtensionArgument` (abstract) → `XamlPositionalArgument` | `XamlNamedArgument` (carries `NameSpan`, `EqualsSpan?`). The argument's `Value` is a `XamlExtensionValue` (abstract) → `XamlExtensionStringValue` (bare-text span) | `XamlExtension` (nested — extension is its own value type, no wrapper needed).
  - Driven by `MarkupExtensionParser` — a standalone mini-parser invoked from `XamlParser.MakeValue`. Operates strictly inside `ContentSpan`, appends diagnostics to the shared list. Keeping it separate from the main lexer/parser keeps the XML grammar uncontaminated by ME grammar and lets the mini-parser carry its own cursor state (line/col tracked the same way as the lexer).
  - Tolerance: unterminated `}` (`IsClosed=false` + diagnostic), empty type name (`{|`), trailing `=`/`,`, double `,`, nested extensions, escape `{}`. `<` and `>` are **barrier characters** in the mini-parser — when the outer attribute value is itself unterminated and `ContentSpan` runs to EOF, the extension span stops at the next `<` / `>` instead of swallowing the rest of the document.
  - The `\}` / `\\` escapes that the XAML markup-extension grammar formally supports are **not** implemented. They are vanishingly rare in vxaml; `\` inside a string value parses as a literal character. Add a `\`-escape branch in `MarkupExtensionParser.ParseValue` if a real use case appears.
- Every node has `Parent` set (root nodes have `Parent == null`). `XamlValue.Parent` is its `XamlAttribute`; an extension's parent chain walks back through any number of nested-extension layers to the `XamlAttribute` (see `XamlPositionClassifier.FindOwningAttribute`).
- Span invariant: a parent's `Span` always contains the spans of its attributes, values, children, and (when present) extension subtree (asserted by `Span_of_every_node_contains_spans_of_its_children`).
- Recovery covered: unclosed tags at EOF, mismatched close tags, inner unclosed tags closed by outer, stray `<` mid-tag, unterminated attribute values, unterminated comments/CDATA, broken sibling not poisoning later good siblings, plus all the markup-extension tolerance cases above. 202 tests in `XamlTolerantParserTests` (parser recovery + `ContextAt` + `EditAt`).

- `XamlDocument` carries `Source` (the original string) and offers `TextOf(TextSpan)` — consumers substring by span without threading the source through every call.
- **`XamlDocument` also carries xmlns view of the document**, computed inline by `XmlnsExtraction` during `XamlParser.Parse`:
  - `ClrPrefixes: IReadOnlyDictionary<string, (string asm, string ns)>` — prefix (`""` for default) → CLR coordinates from `clr-namespace:Foo;assembly=Bar`.
  - `LanguagePrefixes: IReadOnlySet<string>` — prefixes bound to a `http(s)://` URL, marking them as the XAML language namespace (enables `{prefix}:Name`, `{prefix}:Key`, etc).
  - `AssemblyNames: string[]` — unique assemblies referenced by `ClrPrefixes`. Convenience projection.
  - No `XmlnsScope` / `XmlnsBinding` hierarchy / `XmlnsResolver` separate class — that was over-engineered. The data is just three flat fields on the document.

- **`FindNodeAt(int offset)`** — done. `XamlDocument.FindNodeAt(offset) → XamlNode?` returns the most specific node whose span contains the offset, or `null` if outside every root. Recursive descent with `SpanSearch.FindContaining` (binary search, O(log n) per level). Sibling spans are assumed non-overlapping and sorted by Start — guaranteed by the parser.
- **Whitespace-only character data is dropped by the parser** — no `XamlText` is emitted for `Content` tokens whose source is only ` `, `\t`, `\r`, `\n`. Significant text (including text that happens to have surrounding whitespace, like `"  hello  "`) is preserved verbatim. Reason: a language service has no use for whitespace nodes (no completion / hover / diagnostic uses them), and prettified files would otherwise triple the tree size with empty leaves. Consequence: in `FindNodeAt`, an offset inside whitespace between tags returns the parent element (or `null` if between roots), giving handlers immediate context instead of a useless empty leaf.

**Not yet implemented (planned):**
- **More `XamlContextProvider` consumers** — `HoverService`, type-aware diagnostics. `CompletionHandler` already reads from `XamlContextProvider.Complete` (tags / attribute names / enum values); see "Type / context provision" for its current shape and the planned `ContextAt` migration.

---

## Position representation — both offset and line/column

**`TextSpan` carries six fields**: `Start`, `Length` (offset arithmetic, for cheap `Source.Substring`) and `StartLine`, `StartColumn`, `EndLine`, `EndColumn` (LSP-shaped, 0-based, UTF-16 code units per column). The lexer walks the source character by character and tracks both in one pass.

Why both:
- LSP delivers `Position` as `(line, character)`. Storing only offsets forces every request to convert both ways.
- But some semantic logic (forward / backward source scans, substring extraction) inherently wants offsets. Carrying both means each consumer picks the cheaper axis with no `LineMap` round-trip.
- `LineMap` is deleted. The few places that still need an offset from an LSP position call `XamlDocument.OffsetAt(line, column)` (a one-shot linear scan over `Source`).

What the lexer does:
- Carries `int _line, _column`; snapshots them at every token start.
- On `\n`: `_line++`, `_column = 0`. `\r` is swallowed (no column change, no line change) — so CRLF behaves as a single break via `\n`, and lone `\r` is treated as a no-op. Files come from VS editors, where lone `\r` does not occur in practice.
- `Eof()` emits a zero-length span at the current `(_pos, _line, _column)` — the parser uses this to size unclosed elements out to EOF without consulting `_source.Length` separately.

What `XamlDocument` exposes:
- `FindNodeAt(int line, int column)` — line/column only. No offset overload; add one back when a consumer actually needs it.
- `OffsetAt(int line, int column)` — linear scan, used by source-scanning code in `XamlContextProvider`.
- `TextOf(TextSpan)` — substring via offsets.

Span composition helpers on `TextSpan`:
- `FromTo(from, to)` — merge from `from.Start*` to `to.End*`.
- `FromToStart(from, to)` — extend `from` up to (not including) `to.Start*`. Used in recovery (close an unclosed tag at the start of the next sibling).

---

## Position classification

`XamlDocument.ContextAt(line, column) → XamlPositionContext` classifies a cursor position into a structural variant that completion / hover / diagnostics dispatch on. Implemented by `XamlPositionClassifier`. The contexts are a discriminated union of records (`OutsideContext`, `TagNameContext`, `ChildTagNameContext`, `EndTagNameContext`, `TagInteriorContext`, `AttributeNameContext`, `AttributeValueContext`, `ElementContentContext`, `TextContext`, `CommentContext`, `CDataContext`, plus the markup-extension family `ExtensionTypeNameContext`, `ExtensionInteriorContext`, `ExtensionArgNameContext`, `ExtensionArgValueContext`, `ExtensionPositionalArgContext`). Every variant carries the filtered list of diagnostics whose spans contain the cursor — handlers don't refilter.

`ExtensionArgValueContext` is the value slot of a **named** argument only (`Argument` is `XamlNamedArgument`); a positional/bare token has its own `ExtensionPositionalArgContext` (`Argument` is `XamlPositionalArgument`). The split is deliberate: the classifier owns the positional-vs-named discrimination so consumers dispatch on the variant alone — never a pattern-match on `Argument`. (vxaml has no real positional values; a bare token is just a name being typed, so completion treats it like an argument-name slot — see "Type / context provision".)

**Tag-name scope is split into two variants, never one with a nullable container.** Element-name completion lives in a clean 2×2 (scope × whether `<` was typed):

| | content (no `<` yet) | tag name (`<` typed) |
|---|---|---|
| **root scope** | `OutsideContext` | `TagNameContext` (Element?) |
| **child scope** | `ElementContentContext` (Element) | `ChildTagNameContext` (Element?, **Container**) |

`TagNameContext` is root-only and carries **no** container. `ChildTagNameContext.Container` is **non-nullable** — a child always has one. The root-vs-child decision is baked into the type by the classifier, so consumers (`XamlContextProvider.Complete`) dispatch with no guard clauses. The container is derived from the element the `<` sits inside (the partial tag's `OwnerElement`, or the enclosing element when no partial node exists yet, e.g. `<a><|`), **not** from `elem?.OwnerElement` alone — the latter is null in the empty-name case and would misroute a child position to root scope.

Algorithm in three steps:
1. `FindNodeAt(line, column)` once, reused everywhere.
2. If the node is a content / extension node (value, comment, cdata, extension, arg, string-value), classify by its runtime type. Three source-text probes precede the node switch:
   - `=` probe at the attribute level — cursor right after an attribute's `=` with no value yet (`<a b=|`) returns `AttributeValueContext(Value=null)`.
   - Tag-name probe — walk back over name chars; preceded by `<` ⇒ `TagNameContext` (root) or `ChildTagNameContext` (when a container is found), by `</` ⇒ `EndTagNameContext`. Handles the `<Butt|` / `</Butt|` cases where the cursor sits past every span's exclusive End.
   - Attribute-name probe — a value-less attribute's span ends at its name, so a cursor at the name's exclusive End (`<Button Wi|`) — or past every span at EOF — misses the attribute node (FindNodeAt returns the owning element or null, which would misroute to `TagInterior` / `Outside`). When name-chars sit to the left (and it is not a tag name), retry `FindNodeAt(column-1)`; if that is a `XamlAttribute`, return `AttributeNameContext`. This is what makes attribute-name completion fire *while typing* while leaving the empty interior slot (`<Button  |/>`, whitespace to the left) as `TagInterior`.
3. Otherwise switch on the node type (element / attribute / text / etc.). The `XamlExtension` case applies the same `=` probe internally for `{Binding Foo=|}` and decides type-name-vs-interior by checking whether the cursor's offset is inside `[OpenBraceSpan.Start+1, TypeNameSpan.End]`. The argument-node cases emit `ExtensionPositionalArgContext` for a positional argument (and its string value) and `ExtensionArgValueContext` only for a named argument's value.

Boundary convention — "what's to the LEFT of the cursor":
- Cursor at the exclusive end of a name span (tag name, attribute name, extension type name, extension arg name) is still inside that name span. Pinned by `Right_after_tag_name_with_no_whitespace_is_still_TagName` (tags) and `Inside_extension_type_name_is_TypeName` (extensions). The user just finished typing — completion should still see the partial name.
- Cursor on whitespace immediately after a name is the *next* context (Interior / Arg slot). The probe walks back over name chars, so this falls out automatically.

For unclosed extensions specifically, `XamlValue.FindNodeAt` descends into the extension even at its exclusive-end column — without that, `"{|"` (cursor right after `{`) would land on the surrounding `XamlValue` instead of the empty-type-name extension.

---

## On-type formatting (auto-edits)

`XamlDocument.EditAt(line, column, char ch) → XamlTypingEdit?` is the formatting sibling of `ContextAt`: given a freshly typed trigger character (already in `Source`, caret right after it) it returns the auto-edit to apply, or `null`. Implemented by `XamlTypingEditor`; driven by the LSP `textDocument/onTypeFormatting` handler (`OnTypeFormattingHandler`, trigger chars `>` and `/`). Proven end-to-end in VS 2026.

**`XamlTypingEdit(TextSpan Replace, string NewText)`** — a neutral, LSP-free edit; the server maps it 1:1 to a `TextEdit` (`Replace → Range`, `NewText`). **No explicit caret field:** VS 2026 derives the caret from the edit *shape* (verified live, see [memory: onTypeFormatting caret](/.claude/memory/project_ontypeformatting_caret.md)):
- an **empty `Replace` at the caret** leaves the caret to the **left** of `NewText`;
- a **`Replace` whose range END is the caret** moves the caret to the **end** of `NewText`;
- a `Replace` entirely to the **right** of the caret leaves the caret put.

The shape *is* the caret instruction, so a `(span, text)` pair suffices — no desired-caret field was needed.

**Behaviours** (`switch (ch)` in `XamlTypingEditor.Edit`):
- **`>` → insert matching close tag.** `<Button>|` ⇒ `<Button>|</Button>`. Empty `Replace` at the caret, `NewText = "</name>"`. Guarded: the element (from `FindNodeAt`) must be neither self-closing nor already closed; name from `OpenNameSpan`.
- **`/` → complete self-close.** `<Button/|` ⇒ `<Button/>|`. `Replace` of the just-typed `/`, `NewText = "/>"` (range ends at the caret ⇒ caret after `>`).
- **`/` → collapse a redundant close tag.** `<Grid|></Grid>` + `/` ⇒ `<Grid/|>` (the `</Grid>` is deleted). Fires only when, right after the existing `>`, the **literal** text `</name>` stands **immediately** there with a **matching name** — `Replace` = its span, `NewText = ""` (deletion is to the right of the caret, so the caret stays). **Deliberately strict** (see [memory: typical over exhaustive](/.claude/memory/feedback_typical_over_exhaustive.md)): no whitespace skipping, no content analysis — adjacency already implies the element is empty. The **name match is mandatory** — a partial child before a parent's close tag (`<StackPanel><Grid/></StackPanel>`) must not eat `</StackPanel>`.

**Why not route through `ContextAt`.** onTypeFormatting fires *after* the character is in the buffer, with the caret right after it. At that caret the element's span is at its exclusive End (EOF cases `<Button>` / `<Button/`), so `FindNodeAt(caret)` returns `null` and `ContextAt` would say `Outside`. The robust primitive is **`FindNodeAt(column - 1)`** — the position of the typed character, which sits inside the element's span and yields it directly. So `EditAt` reuses `FindNodeAt` (parser owns positions) but does **not** go through the `ContextAt` union. Guards lean on this: a `>` / `/` inside an attribute value or text content resolves to a `XamlValue` / `XamlText`, so those positions never reach an edit. Covered by `EditAtTests`.

---

## Type / context provision

The xmlns view of a document is **already in `XamlDocument`** (see above) — pure data, computed by `XmlnsExtraction` during parse. The rest lives in `XamlLanguageServer`: one server-lifetime resolver, a thin CLR-type abstraction layer, and the context provider.

### `AssemblyResolver` — reads assemblies from `@assemblies`
- Plain `IDisposable` class — no longer inherits `MetadataAssemblyResolver`, no `MetadataLoadContext`.
- Public API: `EnsureFolderFor(string vxamlPath)`, `XamlAssembly? GetAssembly(string name)`, `Dispose()` (no-op — there is nothing to release; we hold only the built `XamlAssembly` strings). No raw reflection type leaks out — `GetAssembly` returns the `XamlAssembly` wrapper.
- `GetAssembly` finds `{name}.dll` in a registered folder, reads its bytes, opens a `PEReader` / `MetadataReader`, builds `XamlAssembly`, caches by name in `_loaded`. **No negative caching:** a miss leaves the cache untouched so a later `EnsureFolderFor` discovery can succeed without an invalidation dance.
- See "Assembly discovery" + "Loading & lookup" above for the walk-up and the metadata-reading details.

### Type abstraction layer — `XamlAssembly` / `XamlType` / `XamlProperty` / `FullXamlType` / `MetaSignature`
Plain data extracted from metadata — **no `System.Type` / `PropertyInfo` / metadata handle is retained** past index construction. All `internal` to `XamlLanguageServer`.
- **`XamlAssembly`** — built from a `MetadataReader`. Iterates `reader.TypeDefinitions`, keeps only top-level public, concrete (non-abstract), named (non-empty namespace), non-interface, non-enum types, and builds a `ByNamespace[ns][typeName] → XamlType` index **once eagerly**. API: `XamlType? GetType(ns, typeName)`, `IEnumerable<XamlType> GetTypes(ns)`. Owns all the metadata-reading helpers — `CollectHierarchy` (base-class names + transitive same-file interface closure in one walk), `ResolveContentElementType`, `ReadSettableProperties`, `ReadEnumValues`, `ReadContentProperty`/`AttributeTypeName` — and the static `name → role` map.
- **`XamlType`** — document-independent, prefix-free **data holder**: `Name`, role flags, the by-name `_baseNames` set, `ContentProperty`, `ContentPropertyType`, `Properties`. Roles are a `[Flags] enum XamlTypeRole` (`RootElement`, `MarkupExtension`, `UiElement`, `Inline`) folded from the base-class names against the role map (by **simple name** — decoupled from namespaces/assemblies; in-dialect collision risk negligible). `IsAChild(name)` = (self-name or anything in `_baseNames` — base classes **and** implemented interfaces, all by simple name) **and** the type is itself usable as a child (not a root element or markup extension — that exclusion is folded in so call sites don't repeat it). `IsVisibleIn(child)` is the content-model authority (see below). `HasTypedContent` = `ContentPropertyType` is a concrete type (not `Object`/null).
- **`XamlProperty`** — settable property surfaced for attribute completion: `Name`, `Type` (the declared type's simple name), `ContentElementType` (the leaf item type when the property is a collection — `Header : TableRowCollection : List<TableRow>` → `TableRow`; **null for scalars**, since only collection properties drive property-element completion), `EnumValues` (member names, non-null only for a same-file enum). `ContentElementType` is decoded by the same `TryResolveCollectionElementType` helper behind `XamlType.ContentPropertyType`.
  - **`ReadSettableProperties` collects the full inherited surface**, not just declared members: it walks `BaseType` while it stays a `TypeDefinition` in this file (`Button : CommandControl : ContentControl : Control : UIElement : …`, all same-assembly `TypeDef`s — see [memory: probe, don't theorize](/.claude/memory/feedback_probe_dont_theorize.md)), accumulating settable properties at each level. Dedup is by name with **first-seen-wins**, so a derived declaration shadows the inherited one. The walk ends at the first external base (the terminal `System.Object` `TypeRef`, or a sibling assembly) — those members live in a file we do not open. **Delegate-typed members are excluded** (`Action`, and the generic `Action<…>` whose metadata name is `Action`N`) — they are code callbacks, never XAML attributes; the check runs *before* the dedup add so it never reserves a name from a settable same-named member higher up. Get-only properties are still skipped (a setter is required).
- **`FullXamlType(XamlType Type, string Prefix)`** — a shared `XamlType` paired with the xmlns **prefix** it is bound to *in a specific document*; carries `QualifiedName` (`prefix:Name`, or bare `Name` for the default binding). The prefix attaches **here**, at enumeration time, because it is the one document-scoped fact — the `XamlType` itself is shared server-wide.
- **`MetaSignature`** — `MetaTypeRef` (a name-only decoded type: simple name, namespace, optional `Def`/`Ref` handle, and the arguments of a generic instantiation) plus `MetaSignatureProvider`, the stateless `ISignatureTypeProvider` / `ICustomAttributeTypeProvider` that decodes signature and custom-attribute blobs into `MetaTypeRef`. This is the `SignatureDecoder` plumbing behind `List\`1<TableRow>` → `TableRow` and the `ContentPropertyAttribute(string)` argument read.

**Content model — `XamlType.IsVisibleIn(child)`** decides what may sit inside a container, dispatched from `XamlContextProvider.ContentEntries` after resolving the container element to its `XamlType` via `XamlContextProvider.ResolveType` (the point-lookup inverse of `ReachableTypes`):
- **Typed content model** (`HasTypedContent`) — the content property has a concrete element type, whether a typed collection (`Rows : TableRowCollection : List<TableRow>`) or an interface element type (`Cells : SheetCells : List<ISheetCell>`). Only children that `IsAChild` that element type are valid, and the coarse `IsUiElement` role pre-filter **steps aside** (a typed child like `TableRow`, or an `ISheetCell` implementer, need not be a `UIElement`). (`IsAChild(name)` = `IsA` the name **and** not a root element / markup extension — the latter exclusion folded in so call sites don't repeat it.)
- **Unconstrained model** — `Object` / mixed content (e.g. `List<Object>`, where the element type carries no real constraint). Fall back to the coarse role filter (`IsUiElement`, minus root elements and markup extensions) plus the `Inlines → IsInline` name convention. Richer per-name convention is added only as new narrow containers appear — not generalised ahead of need (see [memory: typical over exhaustive](/.claude/memory/feedback_typical_over_exhaustive.md)).
- **Property element** `<Owner.Property>` — handled in `ContentEntries` *before* the container-type branch: the container name carries a `.`, so it is split into `Owner` (possibly prefix-qualified, `my:Table.Header`) and `Property`. `Owner` is resolved via `ResolveType`, the `XamlProperty` is found by name, and its `ContentElementType` is the element type children must `IsAChild`. Only **collection** properties qualify (`Header : TableRowCollection` → `TableRow`); a scalar property element (`<Table.GridLines>`) has `ContentElementType == null` and offers nothing — deliberate, only collections are wired (`PropertyElementEntries`).

### `XamlContextProvider` — completion entries for a position
- Constructor takes `AssemblyResolver`. Server-lifetime singleton.
- Public API:
  - `IEnumerable<FullXamlType> ReachableTypes(XamlDocument doc)` — every type reachable through the document's `ClrPrefixes`, each paired with its binding prefix. **No role filtering** — entry builders apply their own. Aggregates across **all** prefixes. Building block; consumed by the entry builders and by tests.
  - `IReadOnlyList<XamlCompletionEntry> Complete(XamlDocument doc, int line, int column)` — the single position-aware entry point. Calls `doc.ContextAt(line, column)` and dispatches via a `switch` over the `XamlPositionContext` variant. Returns already-labelled, already-categorised entries; the handler maps each 1:1 to an LSP `CompletionItem`.
- `XamlCompletionEntry(Label, XamlCompletionKind, Detail?)` + `XamlCompletionKind { Tag, Attribute, EnumValue }` — domain-neutral; `CompletionHandler.MapKind` turns each into a `CompletionItemKind`.

**`Complete` dispatches on `ContextAt`** — the parser owns all position semantics (see "Position classification"); the provider only decides *what to offer*. Implemented arms:
- **`TagNameContext` → root tags / `ChildTagNameContext` + `ElementContentContext` → child tags.** Root vs. child is decided by the **variant**, not a nullable field (see "Position classification" for the 2×2 and why the old single-variant-with-nullable-container shape was split): `TagNameContext` → `RootEntries(doc)` (filters `IsRootElement`); `ChildTagNameContext` or `ElementContentContext` → `ContentEntries(doc, ...)` for the container's content. When the container is a **property element** (`<Table.Header>`), `ContentEntries` routes to `PropertyElementEntries` (see "Content model" above). Labels are prefix-qualified via `FullXamlType.QualifiedName`. Enumerate the full candidate set, never point-look-up the in-progress name — the editor filters (see [memory: no partial-name lookup](/.claude/memory/project_completion_no_partial_lookup.md)).
- **`AttributeNameContext` → settable properties** of the element (`PropertyEntries(ResolveType(doc, anc.Element))`). **`TagInteriorContext` returns empty** — deliberately: the empty interior slot is reached mostly by Backspace/Del re-trigger, where an auto-popped list is noise; the attribute-name probe (see "Position classification") routes *typing* to `AttributeNameContext`, so the list still appears while typing.
- **`ExtensionTypeNameContext` → markup-extension types** (`ExtensionTypeEntries`, filters `IsMarkupExtension`).
- **`ExtensionInteriorContext` / `ExtensionArgNameContext` / `ExtensionPositionalArgContext` → the extension type's properties** (all three via `ExtensionPropertyEntries(doc, ext)`, resolving the owner through the extension's `TypeNameSpan`). All three are "argument name" slots: in the realistic editor flow (quote/brace auto-closed) a freshly typed name keeps the caret at the token end → `Interior`; editing an existing `Name=` → `ArgName`; a bare token → `PositionalArg`. Only `ExtensionArgValueContext` (the value slot of a *named* arg, after `=`) returns empty — enum/bool values land there later.

The remaining arms (`AttributeValueContext`, `ExtensionArgValueContext`, and the no-op `Outside`/`Text`/`Comment`/`CData`/`EndTagName`) return empty for now. The scaffold convention: each arm hands its method the concentrated data it processes (element / attribute / extension), not the bare base context. `PropertyEntries(FullXamlType?)` is the shared property→entry builder; `ExtensionPropertyEntries` is the thin owner-from-extension wrapper over it.

The path of the vxaml file is needed exactly once — on `didOpen`/`didChange`, to call `_resolver.EnsureFolderFor(...)`. After that provider methods take only the parsed `XamlDocument`.

### What is **NOT** in the design (and why)

- **No `ITypeProvider` interface.** Was a YAGNI artefact justified by "two implementations (real + test fake)" — but the singleton lives in the server project, server tests can use a real fixture DLL, no fake provider needed for parser-side tests.
- **No `SemanticModel` / `SemanticModelFactory`.** Their role split between `XamlDocument` (xmlns fields, `FindNodeAt`) and `XamlContextProvider` (types). Adding a third layer was duplication.
- **No `XmlnsScope` / `XmlnsBinding` hierarchy / `XmlnsResolver` separate class.** Three flat fields on `XamlDocument` do the job; the abstract base + two sealed subclasses + scope container were over-engineered.
- **No per-document type provider, no per-document reader.** One server-lifetime `AssemblyResolver` with a by-name `XamlAssembly` cache. Type coordinates are absolute, so one cache serves all documents.
- **No `FileSystemWatcher` on the @assemblies folder.** The by-name cache never reloads a changed DLL, and vxaml types come from a stable controlled package — a server restart is the refresh path. (Note: we no longer lock the DLL — bytes are copied once — so a watcher would now be *possible*, but it is still not wanted: it would only invalidate the cache, which restart already does.)

### What still needs to be built

`Complete` is now on `ContextAt`; the tag arms (`TagNameContext` → root, `ChildTagNameContext` / `ElementContentContext` → child) are implemented, and child tags are content-model filtered via `XamlType.IsVisibleIn` (typed collections / interface element types fully; unconstrained content via the coarse role filter — see "Content model" above). **Property elements** (`<Owner.Property>`) over collection properties are wired (`PropertyElementEntries`). Remaining:

- **Property-element tag-name completion** — completing the `<Table.|` slot itself to offer the owner's collection-property names (`Header`, `Footer`, `Rows`). The *children inside* a property element are done; offering the property element tag name is the companion piece.
- **Inherited content-property / get-only properties** — `ReadSettableProperties` now walks the base-class chain (inherited settable properties *are* surfaced — see the `XamlProperty` note above), but `[ContentProperty]` and the content-element-type read still consult the type itself only (`ReadCustomAttribute(td)`), so a content property *declared on a base class* won't be picked up; get-only properties are still skipped. Acceptable for the current dialect (content collections are declared with setters on the leaf type); revisit if a real case appears.
- **Deeper unconstrained-content convention.** Only the `Inlines → IsInline` name rule exists for `Object`/mixed containers; add more `content-property-name → allowed role` rules as narrow containers appear, but don't generalise ahead of need.
- **Remaining `Complete` arms** — `AttributeValueContext` and `ExtensionArgValueContext` (the named-arg value slot) — both want enum/bool/type-converter values. Attribute-name and the extension argument-name slots are now wired (see "`Complete` dispatches on `ContextAt`"). Each arm follows the same scaffold: receive its concentrated context data, enumerate, never point-look-up the partial name.
- **Markup-extension completion** — extension *type names* are wired (`ExtensionTypeEntries`, `IsMarkupExtension` filter); open design questions remain on the optional `Extension` suffix and `x:`-namespace forms.
- **Value completion beyond enums** — bool (`True`/`False`) and known type-converter values; only enums are wired in `XamlProperty`.
- **`TextEdit` / replacement ranges** — entries return bare `Label`. Prefixed names (`x:Name`, `my:Button`) may need explicit ranges because `:` is a word boundary in VS.

---

## DI & services

- Extension-level services (logging etc.) are registered in `ExtensionEntrypoint.InitializeServices(IServiceCollection)`.
- Register as concrete class (no interface) when there is only one implementation and no mocking need. Extract interface only when multiple implementations or unit test mocks are required.
- Architecture principle: **design correctly from the start**. Do not defer proper abstractions with "we'll refactor later."

---

## CompletionItemKind → icon mapping (preliminary)

| `XamlPositionContext` variant | Kind |
|---|---|
| `TagNameContext` / `ChildTagNameContext` (`<Button>`) | `Class` |
| `AttributeNameContext` | `Property` |
| `AttributeValueContext` | `Value` / `EnumMember` |
| `TagInteriorContext` (attr-name slot) | `Property` |
| `ExtensionTypeNameContext` (`{Bind\|`) | `Class` |
| `ExtensionArgNameContext` (`{Binding Pa\|th=…`) | `Property` |
| `ExtensionArgValueContext` | `Value` / `EnumMember` |
| Namespace prefix | `Module` |

---

## Open issues

- **TextMate grammar delivery in production.** Currently the grammar lives in the VS installation folder and will be overwritten on VS update. The new extensibility model cannot carry TextMate grammars. Needs a companion classic VSIX or installer. Deferred.
- **Two `Complete` arms remain empty** — the value slots (`AttributeValueContext`, named `ExtensionArgValueContext`); see "What still needs to be built". Tag, attribute-name and extension argument-name slots are wired.
- **`XamlLanguageServerTests` is thin.** `XamlContextProviderTests` (real fixture: reads `XamlTolerantParserTests/@assemblies/A2v10.Xaml.dll` through the production `AssemblyResolver` / `MetadataReader`, no fakes) covers `ReachableTypes`, the tag-name / child-tag `Complete` cases, the content model (`Table → TableRow` typed collection; `SheetRow → ISheetCell` interface element type, incl. a child inheriting the interface through a base class), inherited-property collection (`Button` offers `Command`/`Content`/`Width` from up the chain), and extension-property completion. The value-slot arms remain uncovered.
- Debounce / cancellation for push diagnostics.
- Potential version conflict between transitive dependencies of OmniSharp ↔ VS Extensibility SDK. Has not triggered yet — keep in mind.
