using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Clap;

/// <summary>
/// A CLAP plugin hosted as an Ongenet insert effect: audio in → audio out, processed in place.
/// Shared hosting (loading, params, GUI) lives in <see cref="ClapPluginBase"/>.
/// </summary>
public sealed class ClapEffect : ClapPluginBase, IAudioEffect
{
    public ClapEffect(string modulePath, string pluginId, string displayName)
        : base(modulePath, pluginId, displayName) { }

    public bool Enabled { get; set; } = true;

    public void Process(Span<float> buffer) => RenderAudio(buffer, feedInput: true, replace: true);

    public IAudioEffect Clone() => new ClapEffect(ModulePath, PluginId, Name) { Enabled = Enabled };
}
