using System;
using Ongenet.Core.Audio.Effects;

namespace Ongenet.Au;

/// <summary>
/// An Audio Unit (an audio / music effect) hosted as an Ongenet insert effect: audio in → audio out,
/// processed in place. Shared hosting (loading, params, GUI) lives in <see cref="AuPluginBase"/>.
/// </summary>
public sealed class AuEffect : AuPluginBase, IAudioEffect
{
    public AuEffect(uint type, uint subType, uint manufacturer, string displayName)
        : base(type, subType, manufacturer, displayName) { }

    protected override bool FeedsInput => true;

    public bool Enabled { get; set; } = true;

    string IAudioEffect.TypeId => MakeId(Type, SubType, Manufacturer);

    public void Process(Span<float> buffer) => RenderAudio(buffer, feedInput: true, replace: true);

    public IAudioEffect Clone() => new AuEffect(Type, SubType, Manufacturer, Name) { Enabled = Enabled };
}
