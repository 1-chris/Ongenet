namespace Ongenet.Core.Audio.Instruments.Sampler.Sf2;

/// <summary>A preset header record from the <c>phdr</c> chunk (SF2 spec §7.2). The last record is a
/// terminal sentinel ("EOP") and is not a real preset.</summary>
public readonly record struct Sf2PresetHeader(string Name, int Program, int Bank, int PresetBagNdx);

/// <summary>A zone record from a <c>pbag</c>/<c>ibag</c> chunk: indices into the generator and modulator
/// lists. We only use the generator index.</summary>
public readonly record struct Sf2Bag(int GenNdx, int ModNdx);

/// <summary>An instrument header from the <c>inst</c> chunk (§7.6). The last record is terminal ("EOI").</summary>
public readonly record struct Sf2InstHeader(string Name, int BagNdx);

/// <summary>
/// A sample header from the <c>shdr</c> chunk (§7.10): the sample's bounds and loop points (in sample
/// points within the <c>smpl</c> data), its recorded rate, original MIDI pitch and tuning correction, and
/// its stereo linkage/type. The last record is terminal ("EOS").
/// </summary>
public readonly record struct Sf2SampleHeader(
    string Name,
    uint Start,
    uint End,
    uint StartLoop,
    uint EndLoop,
    uint SampleRate,
    int OriginalPitch,
    int PitchCorrection,
    int SampleLink,
    int SampleType);

/// <summary>A selectable preset, mapped back to its <c>phdr</c> index, sorted by bank then program.</summary>
public readonly record struct Sf2PresetRef(int PhdrIndex, int Bank, int Program, string Name);
