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
    public void Complete_in_child_tag_name_slot_offers_content_not_root()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Line 1 is a child tag being typed inside the (open) Page element —
        // a ChildTagNameContext. The container's content is offered; the root
        // element Page must NOT appear (it is root-only).
        const string src =
            "<Page xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<B";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 2);

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(XamlCompletionKind.Tag, e.Kind));
        Assert.DoesNotContain(entries, e => e.Label == "Page");
    }

    [Fact]
    public void Complete_text_content_property()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Line 1 is a child tag being typed inside the (open) Page element —
        // a ChildTagNameContext. The container's content is offered; the root
        // element Page must NOT appear (it is root-only).
        const string src =
            """
            <Text xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml">

            </Text>
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 2, 1);

        Assert.NotEmpty(entries);
        Assert.Equal(13, entries.Count);
    }


    [Fact]
    public void Table_content_property_resolves_element_type_to_TableRow()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        // Table's content property is a TableRowCollection : List<TableRow>. The
        // resolver must dig through the collection's base chain to the leaf
        // element type — so the only tag valid inside <Table> is TableRow.
        var table = ctx.ReachableTypes(doc).FirstOrDefault(t => t.Type.Name == "Table");
        Assert.NotNull(table);
        Assert.Equal("Rows", table!.Type.ContentProperty);
        Assert.Equal("TableRow", table.Type.ContentPropertyType);
    }

    [Fact]
    public void Table_admits_only_TableRow_children()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        var types = ctx.ReachableTypes(doc).Select(t => t.Type).ToList();
        var table = types.First(t => t.Name == "Table");
        var tableRow = types.First(t => t.Name == "TableRow");
        var page = types.First(t => t.Name == "Page");

        // Typed-collection content model: TableRow is admitted, an unrelated
        // element is not.
        Assert.True(table.IsVisibleIn(tableRow));
        Assert.False(table.IsVisibleIn(page));
    }

    [Fact]
    public void Complete_inside_Table_offers_TableRow_not_unrelated_elements()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // A child tag being typed inside <Table> — ChildTagNameContext with
        // Table as the container. Its typed content model (Rows : List<TableRow>)
        // admits TableRow and rejects unrelated elements.
        const string src =
            "<Table xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 1);

        Assert.Contains(entries, e => e.Label == "TableRow");
        Assert.DoesNotContain(entries, e => e.Label == "Page");
    }

    [Fact]
    public void SheetRow_admits_cells_satisfying_the_content_interface()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        // SheetRow is [ContentProperty("Cells")], Cells : SheetCells : List<ISheetCell>.
        // The element type is an INTERFACE, which concrete cells satisfy through
        // implementation, not the base-class chain — so IsAChild must consult
        // implemented interfaces, not only BaseType.
        var types = ctx.ReachableTypes(doc).Select(t => t.Type).ToList();
        var sheetRow = types.First(t => t.Name == "SheetRow");

        Assert.Equal("ISheetCell", sheetRow.ContentPropertyType);

        string[] expected = ["SheetCell", "SheetCellGroup", "SheetGroupCell"];

        var admitted = types.Where(t => sheetRow.IsVisibleIn(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(expected, admitted);
    }

    [Fact]
    public void Collection_property_carries_element_type_scalar_does_not()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        var table = ctx.ReachableTypes(doc).First(t => t.Type.Name == "Table").Type;

        // Header : TableRowCollection : List<TableRow> — a collection property, so
        // ContentElementType is the leaf item type (what may sit in <Table.Header>).
        var header = table.Properties.First(p => p.Name == "Header");
        Assert.Equal("TableRowCollection", header.Type);
        Assert.Equal("TableRow", header.ContentElementType);

        // GridLines is a (scalar) enum — no element type; ContentElementType stays null.
        var gridLines = table.Properties.First(p => p.Name == "GridLines");
        Assert.Null(gridLines.ContentElementType);
    }

    [Fact]
    public void Complete_inside_property_element_offers_the_property_element_type()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // <Table.Header> is a property element; Header is a TableRowCollection :
        // List<TableRow>. A child tag typed inside it must offer TableRow, exactly
        // like the typed content model of <Table> itself.
        const string src =
            """
            <Table xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml">
            <Table.Header>
            <
            </Table.Header>
            </Table>
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 2, 1);

        Assert.Contains(entries, e => e.Label == "TableRow");
        Assert.DoesNotContain(entries, e => e.Label == "Page");
    }

    [Fact]
    public void Complete_inside_scalar_property_element_offers_nothing()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // <Table.GridLines> is a property element over a scalar (enum) property:
        // it has no element type, so child completion offers nothing (deliberate —
        // only collection property elements are wired).
        const string src =
            """
            <Table xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml">
            <Table.GridLines>
            <
            </Table.GridLines>
            </Table>
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 2, 1);

        Assert.Empty(entries);
    }

    [Fact]
    public void Complete_inside_extension_properties()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // <Table.GridLines> is a property element over a scalar (enum) property:
        // it has no element type, so child completion offers nothing (deliberate —
        // only collection property elements are wired).
        const string src =
            """
            <Page xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml"
            Title="{BindCmd }"/>
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 16);

        Assert.Equal(25, entries.Count);
    }

    [Fact]
    public void Button_offers_inherited_properties_from_the_whole_base_chain()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        // Button : CommandControl : ContentControl : Control : UIElement : ...
        // — all TypeDefinitions in this assembly. Settable properties must be
        // collected up the whole chain, not just Button's own declarations.
        var button = ctx.ReachableTypes(doc).First(t => t.Type.Name == "Button").Type;
        var names = button.Properties.Select(p => p.Name).ToHashSet();

        Assert.Contains("Command", names);  // declared on CommandControl
        Assert.Contains("Content", names);  // declared on ContentControl
        Assert.Contains("Width", names);    // declared up at UIElement/Control
    }

    private static string FixtureProjectDir([CallerFilePath] string? thisFile = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile!)!, "..", "XamlTolerantParserTests"));
}
