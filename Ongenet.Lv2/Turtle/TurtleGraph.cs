using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ongenet.Lv2.Interop;

namespace Ongenet.Lv2.Turtle;

/// <summary>The kind of an RDF node: an absolute/relative IRI, a blank node, or a literal value.</summary>
public enum NodeKind { Iri, Blank, Literal }

/// <summary>
/// One RDF node. Subjects and predicates are IRIs or blank nodes; objects may additionally be
/// literals (with an optional datatype IRI / language tag). Value equality makes nodes usable as
/// dictionary keys (literal datatype/lang are part of identity but never appear as subjects).
/// </summary>
public readonly record struct Node(string Value, NodeKind Kind, string? Datatype = null, string? Lang = null)
{
    public bool IsIri => Kind == NodeKind.Iri;
    public bool IsBlank => Kind == NodeKind.Blank;
    public bool IsLiteral => Kind == NodeKind.Literal;

    public static Node Iri(string v) => new(v, NodeKind.Iri);
    public static Node Blank(string v) => new(v, NodeKind.Blank);
    public static Node Lit(string v, string? datatype = null, string? lang = null) => new(v, NodeKind.Literal, datatype, lang);

    public override string ToString() => Kind switch
    {
        NodeKind.Iri => $"<{Value}>",
        NodeKind.Blank => Value,
        _ => $"\"{Value}\""
    };
}

/// <summary>
/// An in-memory RDF triple store with the handful of queries LV2 discovery needs: objects of a
/// (subject, predicate), subjects of a given <c>rdf:type</c>, and typed-literal readers. Built by
/// <see cref="TurtleParser"/>; a bundle merges several files into one graph.
/// </summary>
public sealed class TurtleGraph
{
    private readonly Dictionary<Node, List<KeyValuePair<string, Node>>> _bySubject = new();
    private readonly Dictionary<string, List<Node>> _byType = new(StringComparer.Ordinal);
    private int _blankSeq;
    private int _scopeSeq;

    /// <summary>A graph-unique anonymous blank-node id (so blanks from merged files never collide).</summary>
    public string NewBlankId() => "_:b" + _blankSeq++;

    /// <summary>A per-parse scope id used to namespace file-scoped <c>_:label</c> blank nodes.</summary>
    public int NextScope() => _scopeSeq++;

    public void Add(Node subject, string predicate, Node obj)
    {
        if (!_bySubject.TryGetValue(subject, out var list))
        {
            list = new List<KeyValuePair<string, Node>>();
            _bySubject[subject] = list;
        }

        list.Add(new KeyValuePair<string, Node>(predicate, obj));

        if (predicate == Lv2Api.PredType && obj.IsIri)
        {
            if (!_byType.TryGetValue(obj.Value, out var subjects))
            {
                subjects = new List<Node>();
                _byType[obj.Value] = subjects;
            }

            subjects.Add(subject);
        }
    }

    /// <summary>Every object of (<paramref name="subject"/>, <paramref name="predicate"/>).</summary>
    public IEnumerable<Node> Objects(Node subject, string predicate)
    {
        if (!_bySubject.TryGetValue(subject, out var list)) yield break;
        foreach (var kv in list)
            if (kv.Key == predicate)
                yield return kv.Value;
    }

    /// <summary>The first object of (<paramref name="subject"/>, <paramref name="predicate"/>), or null.</summary>
    public Node? FirstObject(Node subject, string predicate)
    {
        foreach (var o in Objects(subject, predicate)) return o;
        return null;
    }

    /// <summary>Subjects declared with <c>rdf:type</c> <paramref name="typeIri"/>.</summary>
    public IReadOnlyList<Node> SubjectsOfType(string typeIri)
        => _byType.TryGetValue(typeIri, out var s) ? s : (IReadOnlyList<Node>)Array.Empty<Node>();

    /// <summary>True if <paramref name="subject"/> has <c>rdf:type</c> <paramref name="typeIri"/>.</summary>
    public bool HasType(Node subject, string typeIri)
    {
        foreach (var o in Objects(subject, Lv2Api.PredType))
            if (o.IsIri && o.Value == typeIri)
                return true;
        return false;
    }

    /// <summary>The IRI/blank object of a predicate (e.g. lv2:binary), or null.</summary>
    public Node? GetResource(Node subject, string predicate)
    {
        foreach (var o in Objects(subject, predicate))
            if (!o.IsLiteral)
                return o;
        return null;
    }

    /// <summary>The first object's lexical value as a string (literal text or IRI), or null.</summary>
    public string? GetString(Node subject, string predicate)
        => FirstObject(subject, predicate)?.Value;

    /// <summary>The first numeric-literal object parsed as a double (invariant culture), or null.</summary>
    public double? GetDouble(Node subject, string predicate)
    {
        foreach (var o in Objects(subject, predicate))
            if (o.IsLiteral && double.TryParse(o.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        return null;
    }

    /// <summary>The first numeric-literal object parsed as an int (truncating), or null.</summary>
    public int? GetInt(Node subject, string predicate)
    {
        var d = GetDouble(subject, predicate);
        return d is null ? null : (int)Math.Round(d.Value);
    }

    /// <summary>All IRI objects of a predicate (e.g. lv2:portProperty, lv2:requiredFeature).</summary>
    public IReadOnlyList<string> GetIris(Node subject, string predicate)
        => Objects(subject, predicate).Where(o => o.IsIri).Select(o => o.Value).ToList();
}
