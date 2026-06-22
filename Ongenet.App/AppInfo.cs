using System.Reflection;

namespace Ongenet.App
{
    /// <summary>
    /// Application identity surfaced in the UI. The version is read from the assembly's
    /// <see cref="AssemblyInformationalVersionAttribute"/>, which is populated from the solution-wide
    /// <c>&lt;Version&gt;</c> in Directory.Build.props — so the title bar always reflects the build.
    /// </summary>
    public static class AppInfo
    {
        /// <summary>Product name.</summary>
        public const string Name = "Ongenet";

        /// <summary>Version string (e.g. "0.1.0"), with any build metadata trimmed.</summary>
        public static string Version { get; } = ResolveVersion();

        private static string ResolveVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip "+<sourcerevision>" build metadata if a tool (e.g. SourceLink) appended it.
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            var version = assembly.GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
