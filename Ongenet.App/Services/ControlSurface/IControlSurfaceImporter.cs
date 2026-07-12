using System.Collections.Generic;

namespace Ongenet.App.Services.ControlSurface;

/// <summary>Result of importing a third-party control-surface mapping file.</summary>
public sealed class ImportResult
{
    public bool Success { get; init; }
    public string? DefinitionId { get; init; }
    public ImportReport Report { get; init; } = new();
}

/// <summary>Human-readable import summary.</summary>
public sealed class ImportReport
{
    public List<string> Messages { get; } = new();
    public int BindingsImported { get; set; }
    public int BindingsSkipped { get; set; }
}

/// <summary>Best-effort importer for external controller mapping formats.</summary>
public interface IControlSurfaceImporter
{
    string FormatId { get; }
    bool CanImport(string filePath);
    ImportResult Import(string filePath, string outputDirectory);
}
