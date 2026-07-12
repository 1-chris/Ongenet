using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ongenet.App.Services.ControlSurface;

/// <summary>Registry of third-party control-surface importers.</summary>
public sealed class ControlSurfaceImportService
{
    private readonly IReadOnlyList<IControlSurfaceImporter> _importers;

    public ControlSurfaceImportService()
    {
        _importers = new IControlSurfaceImporter[] { new ReaLearnJsonImporter() };
    }

    public IReadOnlyList<IControlSurfaceImporter> Importers => _importers;

    public ImportResult Import(string filePath)
    {
        var importer = _importers.FirstOrDefault(i => i.CanImport(filePath));
        if (importer is null)
        {
            return new ImportResult
            {
                Success = false,
                Report = new ImportReport { Messages = { "Unsupported file format." } }
            };
        }

        return importer.Import(filePath, AppPaths.UserControllersDirectory());
    }
}
