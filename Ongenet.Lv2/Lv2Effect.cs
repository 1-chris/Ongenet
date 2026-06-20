using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Lv2;

/// <summary>
/// An LV2 plugin hosted as an Ongenet insert effect: audio in → audio out, processed in place. Shared
/// hosting (loading, ports, params) lives in <see cref="Lv2PluginBase"/>.
/// </summary>
public sealed class Lv2Effect : Lv2PluginBase, IAudioEffect
{
    public Lv2Effect(Lv2PluginDescriptor descriptor) : base(descriptor) { }

    public bool Enabled { get; set; } = true;

    string IAudioEffect.TypeId => MakeId(Descriptor.Uri);

    public void Process(Span<float> buffer) => RenderAudio(buffer, feedInput: true, replace: true);

    public IAudioEffect Clone() => new Lv2Effect(Descriptor) { Enabled = Enabled };
}
