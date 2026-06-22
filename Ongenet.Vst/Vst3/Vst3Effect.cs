using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Vst.Vst3;

/// <summary>
/// A VST3 plugin hosted as an Ongenet insert effect: audio in → audio out, processed in place. Shared
/// hosting (component/processor/controller, params, GUI) lives in <see cref="Vst3PluginBase"/>.
/// </summary>
public sealed class Vst3Effect : Vst3PluginBase, IAudioEffect
{
    public Vst3Effect(string modulePath, string uid, string displayName)
        : base(modulePath, uid, displayName) { }

    public bool Enabled { get; set; } = true;

    string IAudioEffect.TypeId => MakeId(ModulePath, Uid);

    public void Process(Span<float> buffer) => RenderAudio(buffer, feedInput: true, replace: true);

    public IAudioEffect Clone() => new Vst3Effect(ModulePath, Uid, Name) { Enabled = Enabled };
}
