using System.Collections.Generic;
using System.Linq;
using Xunit;
using XamlTolerantParser;

namespace XamlTolerantParserTests;

public class VisitTests
{
    private static XamlDocument Parse(string src) => XamlParser.Parse(src);

    private static List<XamlNode> Collect(XamlDocument doc)
    {
        var nodes = new List<XamlNode>();
        doc.Visit(nodes.Add);
        return nodes;
    }

    [Fact]
    public void Empty_document_visits_nothing()
    {
        var nodes = Collect(Parse(""));
        Assert.Empty(nodes);
    }

    [Fact]
    public void Visits_every_root()
    {
        var nodes = Collect(Parse("<a/><b/><c/>"));
        var elements = nodes.OfType<XamlElement>().ToList();
        Assert.Equal(3, elements.Count);
    }

    [Fact]
    public void Descends_into_attributes_and_values()
    {
        // Attributes and their values do NOT live in Children — a Children-only
        // walk would miss them. Visit must reach both.
        var nodes = Collect(Parse("<a x=\"1\" y=\"2\"/>"));
        Assert.Equal(2, nodes.OfType<XamlAttribute>().Count());
        Assert.Equal(2, nodes.OfType<XamlValue>().Count());
    }

    [Fact]
    public void Descends_into_nested_children()
    {
        var nodes = Collect(Parse("<a><b><c/></b></a>"));
        Assert.Equal(3, nodes.OfType<XamlElement>().Count());
    }

    [Fact]
    public void Visits_text_comment_and_cdata()
    {
        var nodes = Collect(Parse("<a>hello<!--c--><![CDATA[x]]></a>"));
        Assert.Single(nodes.OfType<XamlText>());
        Assert.Single(nodes.OfType<XamlComment>());
        Assert.Single(nodes.OfType<XamlCData>());
    }

    [Fact]
    public void Descends_into_markup_extension_and_arguments()
    {
        // value -> extension -> arguments -> arg value, none of which are in Children.
        var nodes = Collect(Parse("<a b=\"{Bind Path=X, Mode=OneWay}\"/>"));
        Assert.Single(nodes.OfType<XamlExtension>());
        Assert.Equal(2, nodes.OfType<XamlNamedArgument>().Count());
        // Each named argument carries a string value (X / OneWay).
        Assert.Equal(2, nodes.OfType<XamlExtensionStringValue>().Count());
    }

    [Fact]
    public void Descends_into_nested_extension()
    {
        var nodes = Collect(Parse("<a b=\"{Bind Source={Static Foo}}\"/>"));
        Assert.Equal(2, nodes.OfType<XamlExtension>().Count());
    }

    [Fact]
    public void Pre_order_element_before_its_attribute_and_children()
    {
        var nodes = Collect(Parse("<a x=\"1\"><b/></a>"));
        // Root element is visited first (pre-order), then its attribute, then child.
        Assert.IsType<XamlElement>(nodes[0]);
        var rootIndex = 0;
        var attrIndex = nodes.FindIndex(n => n is XamlAttribute);
        var childIndex = nodes.FindIndex(n => n is XamlElement e && !ReferenceEquals(e, nodes[0]));
        Assert.True(rootIndex < attrIndex);
        Assert.True(attrIndex < childIndex);
    }
}
