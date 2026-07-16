using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Persistence.Import;

/// <summary>Outcome of a foreign project import (always conversion into Ongenet models).</summary>
public sealed class ImportResult
{
    public required Project Project { get; init; }
    public required string SourceFormat { get; init; }
    public required string SourcePath { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnresolvedSamplePaths { get; init; } = Array.Empty<string>();
}
