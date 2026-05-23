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
| `XamlTolerantParser` | Parser, document model, type model. No VS dependencies. *(scaffold only — empty `Class1` placeholder.)* |
| `XamlTolerantParserTests` | xUnit tests for the parser. *(scaffold only.)* |

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

## Assembly resolution (for type model)

Goal: load assemblies referenced via `PackageReference` in the user's project for reflection.

**Approach: `project.assets.json`**

1. Find the `.csproj` via `this.Extensibility.Workspaces().QueryProjectsAsync(...)` with `.WithRequired(p => p.FilesByPath(vxamlFilePath)).With(p => p.Path)`.
2. Assets file: `{csproj_dir}/obj/project.assets.json` — created by `dotnet restore`, does **not** require a build.
3. Parse: `targets[firstTfm][pkg]["compile"]` entries give relative DLL paths. `libraries[pkg]["path"]` gives the package path within the NuGet cache. `packageFolders` gives the cache root(s).
4. Full path: `packageFolder + libPath + relativeDllPath`.
5. Cache: loaded once, invalidated by `FileSystemWatcher` on `project.assets.json` (fires when `dotnet restore` completes).
6. If the file doesn't exist yet → return empty list (lazy, checked on every completion request).

**`findCsproj` delegate** is passed from `XamlLanguageServerProvider` (has `this.Extensibility`) into `XamlLangServer.StartAsync`, then into `AssemblyResolver`. The server project has no VS SDK dependency.

**TODO:** the current delegate ignores `vxamlFilePath` and just returns the first project from `QueryProjectsAsync` — wrong for multi-project solutions. Needs `.WithRequired(p => p.FilesByPath(vxamlFilePath))` once the per-document context is plumbed through.

**TODO:** Consider `SubscribeAsync` on `IProjectSnapshot.ActiveConfigurations.PackageReferences` for VS-level change notification (useful for diagnostics — fires before restore completes). Currently `FileSystemWatcher` is sufficient for cache invalidation.

---

## Parser & document model (not yet implemented)

- **Type model:** reflection via `MetadataLoadContext`. Loaded once at startup, immutable.
- **Parser:** tolerant, full reparse of the tree on every change (non-incremental).
- **Node hierarchy:** `XamlNode` → `XamlElement`, `XamlTextNode`, `XamlMalformedNode`, `XamlAttribute`, `XamlValue`, `XamlExtension`.
- **`FindNodeAt`:** recursive descent with binary search O(log n) per level.

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
- Position classifier — bridge between the document tree and the completion engine.
- Debounce / cancellation for push diagnostics.
- `XamlMalformedNode` needs a reason field.
- Potential version conflict between transitive dependencies of OmniSharp ↔ VS Extensibility SDK. Has not triggered yet — keep in mind.
