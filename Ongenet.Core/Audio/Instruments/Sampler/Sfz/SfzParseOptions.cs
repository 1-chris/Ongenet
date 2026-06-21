using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// Options for <see cref="SfzParser"/>. Keeps the parser pure: file-system access for
/// <c>#include</c> is supplied as a callback so the parser itself is testable in isolation.
/// </summary>
public sealed class SfzParseOptions
{
    /// <summary>
    /// Resolves an <c>#include "path"</c> to the included file's text, or null if it can't be found.
    /// The path is the string as written in the file (resolution against the project directory and
    /// <c>default_path</c> is the resolver's responsibility). When null, includes are skipped and reported.
    /// </summary>
    public Func<string, string?>? IncludeResolver { get; set; }

    /// <summary>Pre-seeded <c>#define</c> macros (keyed including the leading <c>$</c>), if any.</summary>
    public IReadOnlyDictionary<string, string>? Defines { get; set; }

    /// <summary>Guards against runaway or cyclic <c>#include</c> chains.</summary>
    public int MaxIncludeDepth { get; set; } = 16;
}
