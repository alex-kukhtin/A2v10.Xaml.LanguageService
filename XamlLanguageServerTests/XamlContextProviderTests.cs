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

        var entries = ctx.Complete(doc, 1, 2).Entries;

        Assert.NotEmpty(entries);
        // Type entries are all element tags (no enum / attribute leakage); the
        // comment / CDATA snippets are the only non-Tag entries here.
        Assert.All(
            entries.Where(e => e.Kind != XamlCompletionKind.Snippet),
            e => Assert.Equal(XamlCompletionKind.Tag, e.Kind));
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

        // Caret is `<|/Text>` — right after `<`, a child tag slot. Text's content
        // model offers 13 element tags; the comment / CDATA snippets ride along
        // but are not part of the content-model count asserted here.
        var entries = ctx.Complete(doc, 2, 1).Entries;

        Assert.NotEmpty(entries);
        Assert.Equal(13, entries.Count(e => e.Kind == XamlCompletionKind.Tag));
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
    public void TableRow_content_property_resolves_element_type_through_multilevel_base_chain()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        // TableRow is [ContentProperty("Cells")], and Cells is a TableCellCollection
        // : UIElementCollection : List<UIElementBase>. The resolver must walk SEVERAL
        // levels up the collection's base chain (not just the direct base) to the
        // generic List<T> and take its argument — the leaf element type UIElementBase.
        var row = ctx.ReachableTypes(doc).FirstOrDefault(t => t.Type.Name == "TableRow");
        Assert.NotNull(row);
        Assert.Equal("Cells", row!.Type.ContentProperty);
        Assert.Equal("UIElementBase", row.Type.ContentPropertyType);
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

        var entries = ctx.Complete(doc, 1, 1).Entries;

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

        var entries = ctx.Complete(doc, 2, 1).Entries;

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

        var entries = ctx.Complete(doc, 2, 1).Entries;

        Assert.Empty(entries);
    }

    [Fact]
    public void Complete_property_element_tag_name_offers_owner_properties()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // `<Table.|` — the property-element TAG NAME is being typed inside <Table>.
        // The slot offers Table's settable property names, qualified `Table.Prop`
        // (both the scalar GridLines and the collection Header, unlike the CHILDREN
        // inside a property element where only collections qualify). The bare
        // property name must NOT appear — the whole `Table.` is the replaced token.
        const string src =
            """
            <Table xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml">
            <Table.
            </Table>
            """;
        var doc = XamlParser.Parse(src);

        var result = ctx.Complete(doc, 1, 7);

        Assert.Contains(result.Entries, e => e.Label == "Table.GridLines");
        Assert.Contains(result.Entries, e => e.Label == "Table.Header");
        Assert.DoesNotContain(result.Entries, e => e.Label == "GridLines");

        // The replaced token is the whole `Table.` under the caret (dot included,
        // `<` excluded), so accepting `Table.Header` overwrites it cleanly rather
        // than duplicating.
        Assert.Equal(1, result.EditSpan.StartLine);
        Assert.Equal(1, result.EditSpan.StartColumn);
        Assert.Equal(6, result.EditSpan.Length);
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

        var entries = ctx.Complete(doc, 1, 16).Entries;

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

    [Fact]
    public void Complete_in_attribute_value_offers_enum_members()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // GridLines is a (scalar) enum property of Table — the value slot must
        // offer its members as EnumValue entries. Column 11 sits between the two
        // quotes of the empty GridLines="" value on line 1.
        const string src =
            """
            <Table xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml"
            GridLines="" />
            """;
        var doc = XamlParser.Parse(src);

        var result = ctx.Complete(doc, 1, 11);

        Assert.NotEmpty(result.Entries);
        Assert.All(result.Entries, e => Assert.Equal(XamlCompletionKind.EnumValue, e.Kind));

        // The edit range is the empty content BETWEEN the quotes (col 11, length 0),
        // not the quoted region — so accepting a value yields GridLines="None", the
        // quotes intact. This is the deterministic, parser-driven replacement that
        // replaces the editor's word-guess (which ate the quotes).
        Assert.Equal(0, result.EditSpan.Length);
        Assert.Equal(1, result.EditSpan.StartLine);
        Assert.Equal(11, result.EditSpan.StartColumn);
    }

    [Fact]
    public void Complete_in_attribute_value_of_string_property_offers_nothing()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Title is a String property — no enum, no bool: the value slot stays
        // empty (string / type-converter values are not wired).
        const string src =
            """
            <Page xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml"
            Title="" />
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 7).Entries;

        Assert.Empty(entries);
    }

    [Fact]
    public void Tag_name_completion_edit_span_covers_the_partial_name()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // `<Ta` being typed inside Page: the edit range must cover the whole
        // partial name `Ta` (cols 1..3), so accepting `Table` replaces `Ta`
        // rather than inserting after it. This is the same token-replacement path
        // that makes a prefixed name (`my:Ta` -> `my:Table`) work, since ':' is a
        // name char and lives inside OpenNameSpan.
        const string src =
            "<Page xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<Ta";
        var doc = XamlParser.Parse(src);

        var result = ctx.Complete(doc, 1, 3);

        Assert.Contains(result.Entries, e => e.Label == "Table");
        Assert.Equal(1, result.EditSpan.StartLine);
        Assert.Equal(1, result.EditSpan.StartColumn);
        Assert.Equal(2, result.EditSpan.Length); // "Ta"
    }

    [Fact]
    public void Extension_arg_completion_edit_span_covers_the_token_not_the_caret()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        //                  0         1         2
        //                  0123456789012345678901234
        const string src = "<Button Show=\"{Bind Dat}\"></Button>";
        var doc = XamlParser.Parse(src);

        // Caret at col 23 — right after "Dat", at the auto-inserted '}'. The edit
        // range must cover "Dat" (cols 20..23) so accepting "DataType" REPLACES it,
        // rather than inserting at the caret and producing "DatDataType".
        var result = ctx.Complete(doc, 0, 23);

        Assert.Equal(20, result.EditSpan.StartColumn);
        Assert.Equal(3, result.EditSpan.Length);
    }

    [Fact]
    public void Attribute_name_completion_edit_span_covers_the_partial_name()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        //                  0         1
        //                  01234567890
        const string src = "<Button Dis";
        var doc = XamlParser.Parse(src);

        // Caret at col 11 — right after the partial attribute name "Dis" (cols
        // 8..11). The edit range must cover "Dis", not be empty at the caret.
        var result = ctx.Complete(doc, 0, 11);

        Assert.Equal(8, result.EditSpan.StartColumn);
        Assert.Equal(3, result.EditSpan.Length);
    }

    [Fact]
    public void Root_tag_slot_offers_comment_and_cdata_snippets()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Caret right after the root `<` — a TagNameContext. Besides the root
        // element tags, the comment and CDATA snippets are offered. Their body is
        // inserted WITHOUT the leading `<` (already in the buffer).
        var doc = XamlParser.Parse("<");

        var entries = ctx.Complete(doc, 0, 1).Entries;

        var comment = Assert.Single(entries, e => e.Label == "!--");
        Assert.Equal(XamlCompletionKind.Snippet, comment.Kind);
        Assert.Equal("!--  -->", comment.InsertText);

        var cdata = Assert.Single(entries, e => e.Label == "![CDATA[");
        Assert.Equal(XamlCompletionKind.Snippet, cdata.Kind);
        Assert.Equal("![CDATA[]]>", cdata.InsertText);
    }

    [Fact]
    public void Child_tag_slot_offers_comment_and_cdata_snippets()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // `<` typed inside an open Page element — a ChildTagNameContext. The
        // snippets ride along with the child element tags.
        const string src =
            "<Page xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 1).Entries;

        Assert.Contains(entries, e => e.Label == "!--" && e.Kind == XamlCompletionKind.Snippet);
        Assert.Contains(entries, e => e.Label == "![CDATA[" && e.Kind == XamlCompletionKind.Snippet);
    }

    [Fact]
    public void Element_content_slot_without_a_typed_bracket_offers_no_snippets()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Caret on the empty line between the tags — col 0, no `<` typed: an
        // ElementContentContext. A comment body would need a leading `<` that the
        // tag bodies here do not carry, so the snippets stay out of this slot.
        const string src =
            """
            <Text xmlns="clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml">

            </Text>
            """;
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 0).Entries;

        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, e => e.Kind == XamlCompletionKind.Snippet);
    }

    [Fact]
    public void Grid_declares_its_attached_properties_none_transparent_by_default()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));

        var doc = XamlParser.Parse(PageXaml);
        var ctx = new XamlContextProvider(resolver);

        // Grid carries [AttachedProperties("Col,Row,...")], read by name into
        // AttachedProperties. The fixture has no [AttachedPropertyType] overlay yet,
        // so every attached type is "Object" (name completion only, no values), and
        // no type carries [AttachedTransparent], so IsAttachedTransparent is false
        // everywhere — both defaults.
        var grid = ctx.ReachableTypes(doc).First(t => t.Type.Name == "Grid").Type;
        var names = grid.AttachedProperties.Select(p => p.Name).ToArray();
        Assert.Contains("Row", names);
        Assert.Contains("Col", names);
        Assert.All(grid.AttachedProperties, p => Assert.Equal("Object", p.Type));
        Assert.False(grid.IsAttachedTransparent);
    }

    [Fact]
    public void Attached_properties_offered_on_a_direct_child_of_the_container()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Completing an attribute name on a Button that is a direct child of Grid:
        // besides Button's own properties, the container's attached properties are
        // offered owner-qualified (Grid.Row, Grid.Col).
        const string src =
            "<Grid xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<Button D";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 1, 9).Entries;

        Assert.Contains(entries, e => e.Label == "Grid.Row" && e.Kind == XamlCompletionKind.Attribute);
        Assert.Contains(entries, e => e.Label == "Grid.Col");
    }

    [Fact]
    public void Attached_properties_stop_at_a_non_transparent_intermediate()
    {
        using var resolver = new AssemblyResolver();
        resolver.EnsureFolderFor(Path.Combine(FixtureProjectDir(), "any.vxaml"));
        var ctx = new XamlContextProvider(resolver);

        // Text sits under Button under Grid. Button declares no attached properties
        // and is not transparent (the default), so the ancestor walk stops at it —
        // Grid's attached do NOT reach Text. This is the gate's stop branch: by
        // default attached reach direct children only.
        const string src =
            "<Grid xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\">\n<Button>\n<Text X";
        var doc = XamlParser.Parse(src);

        var entries = ctx.Complete(doc, 2, 7).Entries;

        Assert.DoesNotContain(entries, e => e.Label == "Grid.Row");
        Assert.DoesNotContain(entries, e => e.Label == "Grid.Col");
    }

    private static string FixtureProjectDir([CallerFilePath] string? thisFile = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile!)!, "..", "XamlTolerantParserTests"));
}
