using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class XmlnsExtractionTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    [Fact]
    public void Empty_document_has_no_xmlns()
    {
        var d = Parse("");
        Assert.Empty(d.ClrPrefixes);
        Assert.Empty(d.LanguagePrefixes);
        Assert.Empty(d.AssemblyNames);
    }

    [Fact]
    public void No_root_element_has_no_xmlns()
    {
        var d = Parse("<!-- only a comment -->");
        Assert.Empty(d.ClrPrefixes);
        Assert.Empty(d.LanguagePrefixes);
        Assert.Empty(d.AssemblyNames);
    }

    [Fact]
    public void Default_xmlns_with_clr_namespace_is_resolved()
    {
        var d = Parse("<Root xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\"/>");
        Assert.True(d.ClrPrefixes.TryGetValue("", out var b));
        Assert.Equal(("A2v10.Xaml", "A2v10.Xaml"), b);
        Assert.Equal(new[] { "A2v10.Xaml" }, d.AssemblyNames);
    }

    [Fact]
    public void Prefixed_xmlns_with_clr_namespace_is_resolved()
    {
        var d = Parse("<Root xmlns:ui=\"clr-namespace:My.Ui;assembly=My.Ui\"/>");
        Assert.True(d.ClrPrefixes.TryGetValue("ui", out var b));
        Assert.Equal(("My.Ui", "My.Ui"), b);
    }

    [Fact]
    public void Http_xmlns_is_language_binding()
    {
        var d = Parse("<Root xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"/>");
        Assert.Contains("x", d.LanguagePrefixes);
        Assert.Empty(d.ClrPrefixes);
    }

    [Fact]
    public void Https_xmlns_is_language_binding()
    {
        var d = Parse("<Root xmlns:x=\"https://example.com/schema\"/>");
        Assert.Contains("x", d.LanguagePrefixes);
    }

    [Fact]
    public void Language_prefix_can_be_any_name()
    {
        var d = Parse("<Root xmlns:lang=\"http://anything\"/>");
        Assert.Contains("lang", d.LanguagePrefixes);
    }

    [Fact]
    public void Mixed_declarations_populate_independently()
    {
        var d = Parse(
            "<ComponentDictionary " +
            "xmlns=\"clr-namespace:A2v10.Xaml;assembly=A2v10.Xaml\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"/>");
        Assert.Single(d.ClrPrefixes);
        Assert.Single(d.LanguagePrefixes);
        Assert.Contains("", d.ClrPrefixes.Keys);
        Assert.Contains("x", d.LanguagePrefixes);
    }

    [Fact]
    public void Unknown_value_scheme_is_ignored()
    {
        var d = Parse("<Root xmlns:weird=\"ftp://nope\"/>");
        Assert.Empty(d.ClrPrefixes);
        Assert.Empty(d.LanguagePrefixes);
    }

    [Fact]
    public void Clr_namespace_without_assembly_clause_is_ignored()
    {
        var d = Parse("<Root xmlns=\"clr-namespace:Foo\"/>");
        Assert.Empty(d.ClrPrefixes);
    }

    [Fact]
    public void Clr_namespace_with_empty_assembly_is_ignored()
    {
        var d = Parse("<Root xmlns=\"clr-namespace:Foo;assembly=\"/>");
        Assert.Empty(d.ClrPrefixes);
    }

    [Fact]
    public void Clr_namespace_with_empty_namespace_is_ignored()
    {
        var d = Parse("<Root xmlns=\"clr-namespace:;assembly=Bar\"/>");
        Assert.Empty(d.ClrPrefixes);
    }

    [Fact]
    public void Non_xmlns_attributes_are_skipped()
    {
        var d = Parse("<Root Width=\"100\" xmlns=\"clr-namespace:Foo;assembly=Bar\"/>");
        Assert.Single(d.ClrPrefixes);
    }

    [Fact]
    public void Xmlns_on_nested_element_is_ignored()
    {
        var d = Parse(
            "<Root xmlns=\"clr-namespace:Outer;assembly=A\">" +
              "<Child xmlns:y=\"clr-namespace:Inner;assembly=B\"/>" +
            "</Root>");
        Assert.Single(d.ClrPrefixes);
        Assert.False(d.ClrPrefixes.ContainsKey("y"));
        Assert.Equal(new[] { "A" }, d.AssemblyNames);
    }

    [Fact]
    public void Duplicate_prefix_first_declaration_wins()
    {
        var d = Parse(
            "<Root xmlns:p=\"clr-namespace:First;assembly=A\" " +
                  "xmlns:p=\"clr-namespace:Second;assembly=B\"/>");
        Assert.Equal(("A", "First"), d.ClrPrefixes["p"]);
    }

    [Fact]
    public void AssemblyNames_unique_across_prefixes()
    {
        var d = Parse(
            "<Root xmlns=\"clr-namespace:A.Ns1;assembly=A\" " +
                  "xmlns:b=\"clr-namespace:A.Ns2;assembly=A\" " +
                  "xmlns:c=\"clr-namespace:C.Ns;assembly=C\"/>");
        Assert.Equal(new[] { "A", "C" }.OrderBy(s => s), d.AssemblyNames.OrderBy(s => s));
    }

    [Fact]
    public void Xmlns_attribute_without_value_is_skipped()
    {
        var d = Parse("<Root xmlns/>");
        Assert.Empty(d.ClrPrefixes);
        Assert.Empty(d.LanguagePrefixes);
    }
}
