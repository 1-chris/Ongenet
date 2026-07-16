using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Default <see cref="IProjectImportService"/> — first matching importer wins.</summary>
public sealed class ProjectImportService : IProjectImportService
{
    private readonly IReadOnlyList<IProjectImporter> _importers;

    public ProjectImportService(IEnumerable<IProjectImporter> importers)
    {
        _importers = importers.ToList();
    }

    public bool CanImport(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return _importers.Any(i => i.CanImport(path));
    }

    public ImportResult Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var importer = _importers.FirstOrDefault(i => i.CanImport(path))
            ?? throw new NotSupportedException($"No importer registered for '{Path.GetExtension(path)}'.");
        return importer.Import(path);
    }
}
