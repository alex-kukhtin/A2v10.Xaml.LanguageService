// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using XamlLanguageServer;
using XamlTolerantParser;

using Xunit;

namespace XamlLanguageServerTests;

public class XamlContextProviderTests
{
    private const string PageXaml = """
        <Page xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml"
              Title="PageTitle"  />
        """;

    [Fact]
    public void GetRootTypes_yields_Page_with_Title_property()
    {
        using var resolver = new AssemblyResolver();

        // The fixture DLL lives at XamlTolerantParserTests/@assemblies/A2v10.Xaml.dll.
        // Pointing the resolver at any .vxaml path under that folder is enough —
        // it walks up to find @assemblies on its own.
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        var page = ctx.ReachableTypes(doc).FirstOrDefault(t => t.Type.Name == "Page");
        Assert.NotNull(page);
        Assert.Equal(string.Empty, page!.Prefix);

        var title = page.Type.Properties.FirstOrDefault(p => p.Name == "Title");
        Assert.NotNull(title);
        Assert.Equal("String", title!.Type);
        Assert.Null(title.EnumValues);
    }

    [Fact]
    public void Complete_in_tag_name_slot_offers_element_tags()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Line 1 is a child tag being typed inside the (open) Page element.
        // Cursor at column 2 — right after `<B`, a TagNameContext.
        const string src =
            "<Page xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<B";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 2);

        var page = entries.FirstOrDefault(e => e.Label == "Page");
        Assert.NotNull(page);
        Assert.Equal(XamlCompletionKind.Tag, page!.Kind);
    }

    private static string FixtureProjectDir([CallerFilePath] string? thisFile = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile!)!, "..", "XamlTolerantParserTests"));
}
