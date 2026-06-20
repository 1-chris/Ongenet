using System.Linq;
using Ongenet.Lv2.Turtle;

namespace Ongenet.Core.Tests.Lv2;

public class TurtleParserTests
{
    private const string Lv2 = "http://lv2plug.in/ns/lv2core#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    [Fact]
    public void ExpandsPrefixesAndTheTypeKeyword()
    {
        var g = TurtleParser.Parse("""
            @prefix lv2: <http://lv2plug.in/ns/lv2core#> .
            @prefix eg:  <http://example.org/> .
            eg:plugin a lv2:Plugin ; lv2:name "Thing" .
            """);

        var subjects = g.SubjectsOfType(Lv2 + "Plugin");
        Assert.Single(subjects);
        Assert.Equal("http://example.org/plugin", subjects[0].Value);
        Assert.Equal("Thing", g.GetString(Node.Iri("http://example.org/plugin"), Lv2 + "name"));
    }

    [Fact]
    public void ParsesTypedLiteralsAndNumbers()
    {
        var g = TurtleParser.Parse("""
            @prefix eg: <http://example.org/> .
            eg:p eg:i 42 ; eg:f -3.5 ; eg:d 1.0e3 ; eg:s "hi"@en ; eg:t "5"^^<http://www.w3.org/2001/XMLSchema#int> .
            """);

        var p = Node.Iri("http://example.org/p");
        Assert.Equal(42, g.GetInt(p, "http://example.org/i"));
        Assert.Equal(-3.5, g.GetDouble(p, "http://example.org/f")!.Value, 5);
        Assert.Equal(1000.0, g.GetDouble(p, "http://example.org/d")!.Value, 5);
        Assert.Equal("hi", g.GetString(p, "http://example.org/s"));
        Assert.Equal(5, g.GetInt(p, "http://example.org/t"));
    }

    [Fact]
    public void ParsesBlankNodePropertyListsAsSubjects()
    {
        var g = TurtleParser.Parse("""
            @prefix lv2: <http://lv2plug.in/ns/lv2core#> .
            @prefix eg:  <http://example.org/> .
            eg:p lv2:port [ lv2:index 0 ; lv2:symbol "in" ] , [ lv2:index 1 ; lv2:symbol "out" ] .
            """);

        var ports = g.Objects(Node.Iri("http://example.org/p"), Lv2 + "port").ToList();
        Assert.Equal(2, ports.Count);
        Assert.All(ports, n => Assert.True(n.IsBlank));
        var symbols = ports.Select(pt => g.GetString(pt, Lv2 + "symbol")).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "in", "out" }, symbols);
    }

    [Fact]
    public void ParsesCollectionsIntoRdfListChain()
    {
        var g = TurtleParser.Parse("""
            @prefix eg: <http://example.org/> .
            eg:p eg:items ( "a" "b" ) .
            """);

        var head = g.FirstObject(Node.Iri("http://example.org/p"), "http://example.org/items")!.Value;
        Assert.True(head.IsBlank);
        Assert.Equal("a", g.GetString(head, Rdf + "first"));
        var rest = g.FirstObject(head, Rdf + "rest")!.Value;
        Assert.Equal("b", g.GetString(rest, Rdf + "first"));
        Assert.Equal(Rdf + "nil", g.FirstObject(rest, Rdf + "rest")!.Value.Value);
    }

    [Fact]
    public void SkipsCommentsAndHandlesSparqlStyleDirectives()
    {
        var g = TurtleParser.Parse("""
            PREFIX eg: <http://example.org/>
            # a comment line
            eg:p a eg:Thing .  # trailing comment
            """);

        Assert.True(g.HasType(Node.Iri("http://example.org/p"), "http://example.org/Thing"));
    }
}
