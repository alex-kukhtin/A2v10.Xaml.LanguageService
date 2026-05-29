using System;
using System.Collections.Generic;
using System.Linq;

namespace XamlTolerantParser;

// Pulls xmlns declarations from the root element of a parsed document and
// classifies each into a CLR-type-providing binding (clr-namespace:Foo;assembly=Bar)
// or a XAML-language marker (http(s)://...). Runs inline during XamlParser.Parse;
// the parser project owns this because it's pure CLR over the syntax tree.
internal static class XmlnsExtraction
{
    private const String XmlnsAttr = "xmlns";
    private const String XmlnsPrefixed = "xmlns:";
    private const String ClrNamespacePrefix = "clr-namespace:";
    private const String AssemblyEquals = "assembly=";

    public static (
        IReadOnlyDictionary<String, (String asm, String ns)> ClrPrefixes,
        IReadOnlySet<String> LanguagePrefixes,
        String[] AssemblyNames
    ) Extract(IReadOnlyList<XamlNode> roots, String source)
    {
        var clr = new Dictionary<String, (String, String)>(StringComparer.Ordinal);
        var language = new HashSet<String>(StringComparer.Ordinal);

        var root = FirstElement(roots);
        if (root == null)
            return (clr, language, Array.Empty<String>());

        foreach (var attr in root.Attributes)
        {
            if (!TryGetXmlnsPrefix(attr, source, out var prefix))
                continue;
            if (attr.Value == null)
                continue;
            if (clr.ContainsKey(prefix) || language.Contains(prefix))
                continue; // first declaration wins on duplicate prefix

            var value = source.Substring(attr.Value.ContentSpan.Start, attr.Value.ContentSpan.Length);

            if (TryParseClr(value, out var asm, out var ns))
                clr[prefix] = (asm, ns);
            else if (value.StartsWith("http://", StringComparison.Ordinal) ||
                     value.StartsWith("https://", StringComparison.Ordinal))
                language.Add(prefix);
        }

        var assemblies = clr.Values
            .Select(v => v.Item1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (clr, language, assemblies);
    }

    private static XamlElement? FirstElement(IReadOnlyList<XamlNode> roots)
    {
        foreach (var node in roots)
            if (node is XamlElement e)
                return e;
        return null;
    }

    private static Boolean TryGetXmlnsPrefix(XamlAttribute attr, String source, out String prefix)
    {
        var name = source.Substring(attr.NameSpan.Start, attr.NameSpan.Length);
        if (name == XmlnsAttr)
        {
            prefix = String.Empty;
            return true;
        }
        if (name.Length > XmlnsPrefixed.Length && name.StartsWith(XmlnsPrefixed, StringComparison.Ordinal))
        {
            prefix = name.Substring(XmlnsPrefixed.Length);
            return true;
        }
        prefix = String.Empty;
        return false;
    }

    // No whitespace is significant inside an xmlns clr-namespace value — strip
    // it all, then parse with simple StartsWith / IndexOf. This tolerates spaces
    // around `:`, `;`, `=` as well as inside identifiers (the latter shouldn't
    // happen in practice but is harmless to collapse).
    private static Boolean TryParseClr(String value, out String assemblyName, out String clrNamespace)
    {
        assemblyName = String.Empty;
        clrNamespace = String.Empty;

        value = StripWhitespace(value);

        if (!value.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
            return false;

        var rest = value.Substring(ClrNamespacePrefix.Length);
        var semi = rest.IndexOf(';');
        if (semi < 0) return false;

        var ns = rest.Substring(0, semi);
        var tail = rest.Substring(semi + 1);
        if (!tail.StartsWith(AssemblyEquals, StringComparison.Ordinal)) return false;

        var asm = tail.Substring(AssemblyEquals.Length);
        if (ns.Length == 0 || asm.Length == 0) return false;

        clrNamespace = ns;
        assemblyName = asm;
        return true;
    }

    private static String StripWhitespace(String s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (!Char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }
}
