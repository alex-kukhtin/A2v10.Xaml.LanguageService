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
| `XamlTolerantParser` | Lexer, parser, document model, xmlns extraction, `LineMap`, `FindNodeAt`. No VS / no LSP / no I/O dependencies. Pure CLR over source text. |
| `XamlTolerantParserTests` | xUnit tests for parser, xmlns extraction, line map, find-node-at. |

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

---

## Rejected approaches (do not revisit without strong reason)

- **Semantic tokens as TextMate replacement** — pull model breaks coloring on malformed input (i.e. almost every keystroke); valid only as an *additional* layer over TextMate.
- **`ITextViewTaggerProvider` / `ClassificationTag`** — classic MEF API, unavailable in the new VS Extensibility model without a hybrid VSIX.
- **Inheriting `.vxaml` from XML document type** — `DocumentTypeConfiguration.BaseDocumentType` is singular; setting it to XML breaks LSP.

**Root cause of all three:** the new VS Extensibility model is isolated from the classic in-process editor infrastructure. TextMate grammar is the bypass.

---

## Assembly discovery — `@assemblies` convention

User puts the DLLs that the vxaml dialect references (`A2v10.Xaml.dll` etc.) into a folder literally named **`@assemblies`** somewhere above their `.vxaml` files. The server walks up the directory tree from a vxaml path until it finds one. That folder is the type universe for those files. No `project.assets.json`, no `PackageReference`, no `csproj` lookup, no VS Extensibility workspace queries — **pure file-system convention**.

**Discovery flow** in `AssemblyResolver`:
1. On `didOpen`/`didChange`, `TextDocumentSyncHandler` calls `_resolver.EnsureFolderFor(uri.GetFileSystemPath())`.
2. Resolver checks its `_anchors` list — if the new path starts with any known anchor (the *directory containing* `@assemblies`), nothing to do.
3. Otherwise walks `Path.GetDirectoryName` up until a directory with an `@assemblies` subfolder is found. Anchor + folder remembered.
4. If no `@assemblies` exists in the tree, nothing is registered. Lookups will return null silently.

A typical session sees 1-3 walk-ups total. Anchors live for the server lifetime; the per-tree-of-files cost is one `Directory.Exists` per ancestor.

**Loading & lookup** uses `MetadataLoadContext` (chosen for: doesn't lock the DLL — user can rebuild without killing VS; isolated from the host AppDomain — no version conflicts with OmniSharp/VS deps; reflection-only — no type initializers run, no side-effects).

`AssemblyResolver` *inherits* `MetadataAssemblyResolver` so the MLC delegates back to it on every assembly probe (explicit `LoadFromAssemblyName` and implicit cross-assembly type refs). The `Resolve(context, AssemblyName)` override first walks the registered `_folders` set, then falls back to `RuntimeEnvironment.GetRuntimeDirectory()` for anything not present in `@assemblies`.

The runtime-directory fallback is required: the `MetadataLoadContext` constructor immediately calls `Resolve(ctx, new AssemblyName("mscorlib"))` to find the core assembly (System.Object's home) and throws if it gets `null`. Our `Resolve` satisfies that probe via the runtime-directory fallback — without it the MLC won't even construct. The same fallback also covers transitive `System.*` references when a user DLL's `PropertyType` chains back into BCL types. User DLLs in `@assemblies` win over the runtime directory because `_folders` is probed first — a user assembly named like a system one would shadow it (rare but possible). Enum value inspection — frequently needed — does **not** require runtime DLLs (see [memory: MLC enum inspection](/.claude/memory/project_mlc_enum_inspection.md)): use `GetFields(Public|Static)` + `GetRawConstantValue()`, never `Enum.GetValues` or `typeof()`.

**Stale cache:** an already-loaded `Assembly` inside MLC stays even if the DLL on disk changes — there's no reload. Acceptable: vxaml types come from a stable controlled package; restart VS to pick up a new DLL version. No `FileSystemWatcher` needed.

---

## Parser & document model

**Syntax layer (done).** `XamlTolerantParser`:
- Tolerant, full reparse on every change (non-incremental). `XamlParser.Parse(string) → XamlDocument`.
- Lexer is hand-written (`XamlLexer`) with one-token pushback (`Back()`).
- Node hierarchy: `XamlNode` → `XamlElement`, `XamlText`, `XamlComment`, `XamlCData`, `XamlAttribute`, `XamlValue`.
  - `XamlAttribute` is **not** in `element.Children` — it lives in `element.Attributes`. Children are content nodes only.
  - `XamlValue` is owned by `XamlAttribute.Value`. Carries `Span` (whole value with quotes), `ContentSpan` (inside quotes), `Quote`, `IsUnterminated`.
  - No `XamlMalformedNode` — malformed input is represented by diagnostics plus best-effort recovery into normal nodes, not by a distinct node type.
  - No `XamlExtension` yet — markup extensions (`{Binding ...}`) currently end up as plain attribute values. Promoting them to structured nodes is a future task.
- Every node has `Parent` set (root nodes have `Parent == null`). `XamlValue.Parent` is its `XamlAttribute`.
- Span invariant: a parent's `Span` always contains the spans of its attributes, values, and children (asserted by `Span_of_every_node_contains_spans_of_its_children`).
- Recovery covered: unclosed tags at EOF, mismatched close tags, inner unclosed tags closed by outer, stray `<` mid-tag, unterminated attribute values, unterminated comments/CDATA, broken sibling not poisoning later good siblings. 54 tests in `XamlTolerantParserTests`.

- `XamlDocument` carries `Source` (the original string) and offers `TextOf(TextSpan)` — consumers substring by span without threading the source through every call.
- **`XamlDocument` also carries xmlns view of the document**, computed inline by `XmlnsExtraction` during `XamlParser.Parse`:
  - `ClrPrefixes: IReadOnlyDictionary<string, (string asm, string ns)>` — prefix (`""` for default) → CLR coordinates from `clr-namespace:Foo;assembly=Bar`.
  - `LanguagePrefixes: IReadOnlySet<string>` — prefixes bound to a `http(s)://` URL, marking them as the XAML language namespace (enables `{prefix}:Name`, `{prefix}:Key`, etc).
  - `AssemblyNames: string[]` — unique assemblies referenced by `ClrPrefixes`. Convenience projection.
  - No `XmlnsScope` / `XmlnsBinding` hierarchy / `XmlnsResolver` separate class — that was over-engineered. The data is just three flat fields on the document.

- **`FindNodeAt(int offset)`** — done. `XamlDocument.FindNodeAt(offset) → XamlNode?` returns the most specific node whose span contains the offset, or `null` if outside every root. Recursive descent with `SpanSearch.FindContaining` (binary search, O(log n) per level). Sibling spans are assumed non-overlapping and sorted by Start — guaranteed by the parser.
- **Whitespace-only character data is dropped by the parser** — no `XamlText` is emitted for `Content` tokens whose source is only ` `, `\t`, `\r`, `\n`. Significant text (including text that happens to have surrounding whitespace, like `"  hello  "`) is preserved verbatim. Reason: a language service has no use for whitespace nodes (no completion / hover / diagnostic uses them), and prettified files would otherwise triple the tree size with empty leaves. Consequence: in `FindNodeAt`, an offset inside whitespace between tags returns the parent element (or `null` if between roots), giving handlers immediate context instead of a useless empty leaf.

**Not yet implemented (planned):**
- **Markup extensions as structured nodes** — `XamlExtension` with positional/named args, parsed out of attribute values.
- **`XamlContextProvider` consumers** — `CompletionService`, `HoverService`, type-aware diagnostics. The provider is built; nothing reads from it yet (`CompletionHandler` still hard-coded).

---

## Position representation — line/column native, no offsets

**Spans in the parse tree must carry `(line, column)` directly, not character offsets.** The lexer walks the source character by character and already sees every `\n` — tracking line/column at that point is free.

Why this matters:
- LSP delivers `Position` as `(line, character)`. Storing offsets forces every request to convert both ways.
- The original parser scanned positions as integer offsets and the server reconstructs a `LineMap` on every request to translate. That's **two passes over the source where one suffices** — lex once, emit positions in the shape the protocol already uses.
- `LineMap` exists only as a band-aid for the wrong span shape. With native line/column it is deleted; with it goes `LineMap.ToOffset` / `ToLineColumn` and the offset arithmetic in `TextDocumentSyncHandler` and `CompletionHandler`.

What the refactor touches:
- `TextSpan` → `(startLine, startColumn, endLine, endColumn)`. No `Length` int; if substring extraction is needed, `XamlDocument.TextOf(span)` does the line walk in one place.
- `XamlLexer` carries `int _line, _column`, snapshots them on token emission, increments on `\n`.
- `XamlDocument.FindNodeAt` takes `(line, column)`.
- Handlers pass `request.Position.Line, request.Position.Character` straight in; no `LineMap` involved.

Do not preserve offset spans "for compatibility" — every consumer (parser, server, tests) is in this repo and gets migrated atomically.

---

## Type / context provision

The xmlns view of a document is **already in `XamlDocument`** (see above) — pure data, computed by `XmlnsExtraction` during parse. The rest is two singletons in `XamlLanguageServer`, both server-lifetime:

### `AssemblyResolver` — loads assemblies from `@assemblies`
- Inherits `MetadataAssemblyResolver`. Owns the singleton `MetadataLoadContext` (`new MetadataLoadContext(this)`).
- Public API: `EnsureFolderFor(string vxamlPath)`, `Assembly? LoadAssembly(string name)`, `Dispose()`. That's it. No type lookups, no namespace listings.
- See "Assembly discovery" section above.

### `XamlContextProvider` — types by (assembly, namespace)
- Constructor takes `AssemblyResolver`. Singleton.
- Public API:
  - `Type? GetType(string asm, string ns, string typeName)` — point lookup
  - `IEnumerable<Type> GetTypes(string asm, string ns)` — for completion candidates
- Internal cache: `Dictionary<assemblyName, AssemblyCacheItem>`. `AssemblyCacheItem` holds the `Assembly` plus a nested `ByNamespace[ns][typeName] → Type` index, **built once eagerly** when the assembly is first loaded (one pass over `assembly.GetTypes()`, filter `IsPublic && Namespace != null`).
- **No negative caching anywhere.** A missing assembly stays uncached (so later `EnsureFolderFor` discovery succeeds without an invalidation dance). A missing type means "namespace dict doesn't have that name" — also nothing to evict. Repeated misses cost a `LoadFromAssemblyName` + `FileNotFoundException` round trip, but only when the user's xmlns points at a non-existent DLL — rare in practice.

### Handler shape (target)

```csharp
var doc = _store.Get(uri);                              // already parsed, has xmlns fields
foreach (var (prefix, (asm, ns)) in doc.ClrPrefixes)
    foreach (var t in _context.GetTypes(asm, ns))
        candidates.Add(MakeCompletionItem(prefix, t));
```

The path of the vxaml file is needed exactly once — on `didOpen`/`didChange`, to call `_resolver.EnsureFolderFor(...)`. After that the path's job is done; provider methods take only the absolute coordinates `(asm, ns, name)`. No path is threaded through the type API.

### What is **NOT** in the design (and why)

- **No `ITypeProvider` interface.** Was a YAGNI artefact justified by "two implementations (real + test fake)" — but the singleton lives in the server project, server tests can use a real fixture DLL, no fake provider needed for parser-side tests.
- **No `SemanticModel` / `SemanticModelFactory`.** Their role split between `XamlDocument` (xmlns fields, `FindNodeAt`) and `XamlContextProvider` (types). Adding a third layer was duplication.
- **No `XmlnsScope` / `XmlnsBinding` hierarchy / `XmlnsResolver` separate class.** Three flat fields on `XamlDocument` do the job; the abstract base + two sealed subclasses + scope container were over-engineered.
- **No per-document type provider, no per-document MLC.** One singleton MLC for the whole server. Type coordinates are absolute, so a singleton cache serves all documents.
- **No `FileSystemWatcher` on the @assemblies folder.** `Assembly.LoadFrom` lock and MLC's own caching mean reloads need a server restart anyway; the watcher would be lying about reactivity.

### What still needs to be built

- **`CompletionService`** (the first real consumer of `XamlContextProvider`). When implemented: must NOT call `GetType` for in-progress names like `<Butt|` — use `GetTypes` + prefix filter instead. See [memory: no partial-name lookup](/.claude/memory/project_completion_no_partial_lookup.md).
- **Attribute completion** — `XamlContextProvider` exposes types; properties come from `type.GetProperties()` (pure reflection, no extra service layer needed).
- **Value completion for enums** — see [memory: MLC enum inspection](/.claude/memory/project_mlc_enum_inspection.md) for the metadata-only patterns.

---

## DI & services

- Extension-level services (logging etc.) are registered in `ExtensionEntrypoint.InitializeServices(IServiceCollection)`.
- Register as concrete class (no interface) when there is only one implementation and no mocking need. Extract interface only when multiple implementations or unit test mocks are required.
- Architecture principle: **design correctly from the start**. Do not defer proper abstractions with "we'll refactor later."

---

## CompletionItemKind → icon mapping (preliminary)

| Context | Kind |
|---|---|
| Element name (`<Button>`) | `Class` |
| Attribute | `Property` |
| Attribute value | `Value` / `EnumMember` |
| Namespace prefix | `Module` |
| Markup extension (`{Binding}`) | `Keyword` / `Function` |

---

## Open issues

- **TextMate grammar delivery in production.** Currently the grammar lives in the VS installation folder and will be overwritten on VS update. The new extensibility model cannot carry TextMate grammars. Needs a companion classic VSIX or installer. Deferred.
- **`CompletionService` not built.** `CompletionHandler` returns a hard-coded stub. The full type / context infrastructure is ready — `XamlDocument.ClrPrefixes` + `XamlContextProvider.GetTypes` give everything a completion service needs.
- **`XamlLanguageServerTests` project doesn't exist.** `AssemblyResolver` and `XamlContextProvider` are currently un-tested in isolation. When written, the tests need: a temp directory with a synthesized `@assemblies/X.dll` (copy from a fixture project) + a fake `.vxaml` path under it. Real `MetadataLoadContext` should be used — no fakes — that's the whole point of these classes.
- Debounce / cancellation for push diagnostics.
- Potential version conflict between transitive dependencies of OmniSharp ↔ VS Extensibility SDK. Has not triggered yet — keep in mind.
