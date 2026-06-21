using System.IO;

namespace Ongenet.Core.Tests.Sf2;

/// <summary>Locates the bundled <c>GeneralUser-GS.sf2</c> test asset by walking up from the test binary.
/// Tests that need a real SoundFont skip themselves when it isn't present.</summary>
internal static class Sf2TestFile
{
    public const string Name = "GeneralUser-GS.sf2";

    public static string? Find()
    {
        var dir = System.AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, Name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
