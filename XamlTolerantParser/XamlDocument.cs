using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlDocument
{
    public String Source { get; }
    public IReadOnlyList<XamlNode> Roots { get; }
    public IReadOnlyList<XamlDiagnostic> Diagnostics { get; }

    // xmlns view of the document, extracted from root element attributes during Parse.
    // ClrPrefixes maps an xmlns prefix ("" for default) to the (assembly, CLR namespace)
    // it provides. LanguagePrefixes lists prefixes bound to a http(s):// URL — they
    // enable the XAML language attributes (Name, Key, Class, ...) on that prefix.
    // AssemblyNames is the unique set of assemblies referenced by ClrPrefixes.
    public IReadOnlyDictionary<String, (String asm, String ns)> ClrPrefixes { get; }
    public IReadOnlySet<String> LanguagePrefixes { get; }
    public String[] AssemblyNames { get; }

    public XamlDocument(
        String source,
        IReadOnlyList<XamlNode> roots,
        IReadOnlyList<XamlDiagnostic> diagnostics,
        IReadOnlyDictionary<String, (String asm, String ns)> clrPrefixes,
        IReadOnlySet<String> languagePrefixes,
        String[] assemblyNames)
    {
        Source = source;
        Roots = roots;
        Diagnostics = diagnostics;
        ClrPrefixes = clrPrefixes;
        LanguagePrefixes = languagePrefixes;
        AssemblyNames = assemblyNames;
    }

    public String TextOf(TextSpan span) =>
        span.Length > 0 ? Source.Substring(span.Start, span.Length) : String.Empty;

    // Returns the most specific node whose span contains offset, or null if
    // offset falls outside every root (typical for inter-root whitespace or EOF).
    public XamlNode? FindNodeAt(Int32 offset)
    {
        var i = SpanSearch.FindContaining(Roots, offset, static n => n.Span);
        return i >= 0 ? Roots[i].FindNodeAt(offset) : null;
    }
}
