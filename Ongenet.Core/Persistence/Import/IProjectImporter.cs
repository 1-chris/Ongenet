using System.IO;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Reads a foreign DAW project into an Ongenet <see cref="Models.Audio.Project"/> (conversion-only).</summary>
public interface IProjectImporter
{
    /// <summary>Stable format id (e.g. <c>flp</c>, <c>als</c>, <c>dawproject</c>, <c>bwproject</c>).</summary>
    string FormatId { get; }

    /// <summary>True when this importer can handle <paramref name="path"/> based on extension / magic.</summary>
    bool CanImport(string path);

    /// <summary>Parse and map the file at <paramref name="path"/>.</summary>
    ImportResult Import(string path);
}

/// <summary>Dispatches to the first registered <see cref="IProjectImporter"/> that accepts the path.</summary>
public interface IProjectImportService
{
    /// <summary>True when any importer claims <paramref name="path"/>.</summary>
    bool CanImport(string path);

    /// <summary>Import via the matching importer, or throw if none match.</summary>
    ImportResult Import(string path);
}
