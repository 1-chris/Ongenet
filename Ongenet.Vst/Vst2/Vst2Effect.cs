using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Vst.Vst2;

/// <summary>
/// A VST2 plugin hosted as an Ongenet insert effect: audio in → audio out, processed in place. Shared
/// hosting (loading, params, GUI) lives in <see cref="Vst2PluginBase"/>.
/// </summary>
public sealed class Vst2Effect : Vst2PluginBase, IAudioEffect
{
    public Vst2Effect(string modulePath, string uid, string displayName)
        : base(modulePath, uid, displayName) { }

    public bool Enabled { get; set; } = true;

    string IAudioEffect.TypeId => MakeId(ModulePath, Uid);

    public void Process(Span<float> buffer) => RenderAudio(buffer, feedInput: true, replace: true);

    public IAudioEffect Clone() => new Vst2Effect(ModulePath, Uid, Name) { Enabled = Enabled };
}
