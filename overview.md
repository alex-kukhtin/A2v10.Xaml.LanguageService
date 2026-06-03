# A2v10 Xaml Language Service

IntelliSense for the **`.vxaml`** dialect of the [A2v10](https://github.com/alex-kukhtin)
platform — the XAML-flavoured markup behind A2v10 application UI (not WPF `.xaml` or
Avalonia `.axaml`).

## What you get

- **Completion** — element tags, attribute names, attribute values (enums / booleans) and
  markup-extension types and arguments, drawn from the real types your files reference and
  filtered by the content model.
- **Syntax colouring** — tag names, attribute names, values, markup extensions, comments
  and CDATA.
- **On-type formatting** — matching close tags, self-close completion and auto-closing
  pairs.
- **Diagnostics** — unterminated values, mismatched tags and malformed markup extensions,
  reported as you type.

## Setup

Put the assemblies your `.vxaml` files reference (e.g. `A2v10.Xaml.dll`) into a folder
named **`@assemblies`** somewhere above them. The extension walks up the directory tree to
find it — no project or package configuration required.

```
MyProject/
├── @assemblies/
│   └── A2v10.Xaml.dll
└── Views/
    └── Index.vxaml
```

Restart Visual Studio after installing; `.vxaml` files are then recognised automatically.

## Links

- [Source & documentation](https://github.com/alex-kukhtin/A2v10.Xaml.LanguageService)
- [License: MIT](https://github.com/alex-kukhtin/A2v10.Xaml.LanguageService/blob/master/LICENSE.txt)

Requires Visual Studio 2026.
