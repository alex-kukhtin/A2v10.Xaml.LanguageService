# A2v10.Xaml.LanguageService

A Language Server and Visual Studio 2026 extension that brings rich IntelliSense to the
**`.vxaml`** dialect used by the [A2v10](https://github.com/alex-kukhtin) platform.

`.vxaml` is the XAML-flavoured markup language behind A2v10 application UI. It is *not*
WPF `.xaml` or Avalonia `.axaml` — it has its own type universe (`A2v10.Xaml.dll` and
friends) and its own conventions. This project gives that dialect a first-class editing
experience: completion, syntax colouring, on-type formatting and diagnostics, all driven
by a tolerant parser and the assemblies your files actually reference.

## Installation

The extension is published on the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=OleksandrKukhtin.a2v10xamlsp).

Install it from within Visual Studio 2026: open **Extensions → Manage Extensions**, search
for **"A2v10 Xaml Language Service"**, and click Install. Restart Visual Studio to activate
it; `.vxaml` files are then recognised automatically.

## Features

- **Completion** — element tags, attribute names, attribute values (enums / booleans),
  markup-extension type names and arguments. Candidates come from the real types in your
  referenced assemblies, content-model aware (only valid children are offered inside a
  container).
- **Syntax colouring** — semantic highlighting of tag names, attribute names, values,
  markup extensions, comments and CDATA.
- **On-type formatting** — auto-insert of matching close tags (`<Button>` → `</Button>`),
  self-close completion, redundant close-tag collapsing, and auto-closing pairs
  (`"` `'` `` ` `` `{`).
- **Diagnostics** — surfaced from the tolerant parser (unterminated values, mismatched
  tags, malformed markup extensions, …).
- **Tolerant parsing** — every keystroke yields a usable tree, so the editor stays
  responsive on incomplete or malformed input.

## How it works

Everything is served by **one in-process [OmniSharp](https://github.com/OmniSharp/csharp-language-server-protocol)
LSP server** hosted inside the VS extension. The editor talks LSP only — colouring,
completion, on-type edits and diagnostics all flow through it (there is no separate
TextMate layer).

### Type discovery — the `@assemblies` convention

The language service learns the available types from the assemblies your `.vxaml` files
reference. Put those DLLs (e.g. `A2v10.Xaml.dll`) into a folder literally named
**`@assemblies`** somewhere above your `.vxaml` files:

```
MyProject/
├── @assemblies/
│   └── A2v10.Xaml.dll
└── Views/
    └── Index.vxaml      ← server walks up, finds @assemblies, uses it
```

The server walks up the directory tree from each opened file until it finds an
`@assemblies` folder — no `csproj`, `project.assets.json` or package restore required.
Assemblies are read via `System.Reflection.Metadata` (bytes are copied once, so the DLLs
are never locked — you can rebuild them without closing Visual Studio).

> Type metadata is cached for the lifetime of the server. To pick up a new DLL version,
> restart Visual Studio.

## Projects

| Project | Role |
|---|---|
| `XamlServiceExtension` | VSIX entry point. VS Extensibility SDK 17.14. Hosts the LSP server in-process. |
| `XamlLanguageServer` | LSP server library (OmniSharp): handlers, type resolution, document store. |
| `XamlTolerantParser` | Lexer, parser, document model, xmlns extraction, position classification. Pure CLR over source text — no VS / LSP / I/O dependencies. |
| `XamlTolerantParserTests` | xUnit tests for the parser, position classification and on-type edits. |
| `XamlLanguageServerTests` | xUnit tests for type resolution, completion and semantic tokens (against a real fixture assembly). |

## Building

Requirements:

- Visual Studio 2026 with the **Visual Studio extension development** workload
- .NET 8 SDK

```powershell
dotnet build A2v10.Xaml.LanguageService.slnx
```

Press **F5** on the `XamlServiceExtension` project to launch an experimental Visual Studio
instance with the extension loaded; because the LSP server is hosted in-process, you can
set breakpoints in the server and parser directly.

To run the test suites:

```powershell
dotnet test
```

## Status

Active development. Completion (tags, attributes, values, markup extensions), semantic
colouring, on-type formatting and tolerant parsing with diagnostics are working
end-to-end in Visual Studio 2026. Hover and go-to-definition are planned. A standalone
stdio host for VS Code is on the roadmap — the parser and server projects already have no
VS-specific dependencies, so the port is an additional thin host, not a rewrite.

## License

[MIT](LICENSE.txt) — Copyright © 2026 Oleksandr Kukhtin (with Claude).
