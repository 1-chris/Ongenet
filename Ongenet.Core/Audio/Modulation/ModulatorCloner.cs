namespace Ongenet.Core.Audio.Modulation;

/// <summary>Deep-copies modulator state for undo, clone, and persistence roundtrips.</summary>
public static class ModulatorCloner
{
    public static IModulator Clone(IModulator source, IModulatorRegistry registry)
    {
        var copy = registry.Create(source.TypeId);
        copy.Enabled = source.Enabled;
        CopyState(source, copy);
        return copy;
    }

    public static IModulator Clone(IModulator source) => Clone(source, new ModulatorRegistry());

    public static void CopyState(IModulator from, IModulator to)
    {
        switch (from)
        {
            case FourStageModulator s when to is FourStageModulator d:
                d.Attack = s.Attack; d.Hold = s.Hold; d.Decay = s.Decay; d.Curve = s.Curve;
                d.Rate = s.Rate; d.TempoSync = s.TempoSync; break;
            case AdsrModulator s when to is AdsrModulator d:
                d.Attack = s.Attack; d.Decay = s.Decay; d.Sustain = s.Sustain;
                d.Release = s.Release; d.CycleBeats = s.CycleBeats; break;
            case AhdOnReleaseModulator s when to is AhdOnReleaseModulator d:
                d.Attack = s.Attack; d.Hold = s.Hold; d.Decay = s.Decay; break;
            case AhdsrModulator s when to is AhdsrModulator d:
                d.Attack = s.Attack; d.Hold = s.Hold; d.Decay = s.Decay;
                d.Sustain = s.Sustain; d.Release = s.Release; break;
            case CurvesModulator s when to is CurvesModulator d:
                d.Delay = s.Delay; d.Attack = s.Attack; d.Hold = s.Hold;
                d.Decay = s.Decay; d.Curve = s.Curve; break;
            case EnvelopeFollowerModulator s when to is EnvelopeFollowerModulator d:
                d.Attack = s.Attack; d.Release = s.Release; break;
            case RampModulator s when to is RampModulator d:
                d.PeriodBeats = s.PeriodBeats; d.Reverse = s.Reverse; break;
            case SegmentsModulator s when to is SegmentsModulator d:
                d.Levels = (double[])s.Levels.Clone(); d.Rate = s.Rate; d.TempoSync = s.TempoSync; break;
            case LfoModulator s when to is LfoModulator d:
                d.Rate = s.Rate; d.TempoSync = s.TempoSync; d.Wave = s.Wave; d.PhaseOffset = s.PhaseOffset; break;
            case ClassicLfoModulator s when to is ClassicLfoModulator d:
                d.Rate = s.Rate; d.Wave = s.Wave; break;
            case BeatLfoModulator s when to is BeatLfoModulator d:
                d.RateBeats = s.RateBeats; d.Wave = s.Wave; d.Shuffle = s.Shuffle; break;
            case WavetableLfoModulator s when to is WavetableLfoModulator d:
                d.Rate = s.Rate; d.TempoSync = s.TempoSync; d.Shape = s.Shape; break;
            case RandomModulator s when to is RandomModulator d:
                d.Rate = s.Rate; d.TempoSync = s.TempoSync; break;
            case SampleHoldModulator s when to is SampleHoldModulator d:
                d.Rate = s.Rate; d.TempoSync = s.TempoSync; break;
            case StepsModulator s when to is StepsModulator d:
                d.StepCount = s.StepCount; d.RateBeats = s.RateBeats; break;
            case ButtonModulator s when to is ButtonModulator d:
                d.Pressed = s.Pressed; break;
            case ButtonsModulator s when to is ButtonsModulator d:
                d.Active = s.Active; d.Values = (double[])s.Values.Clone(); break;
            case MacroModulator s when to is MacroModulator d:
                d.Value = s.Value; break;
            case Macro4Modulator s when to is Macro4Modulator d:
                d.M1 = s.M1; d.M2 = s.M2; d.M3 = s.M3; d.M4 = s.M4; d.Select = s.Select; break;
            case ExpressionsModulator s when to is ExpressionsModulator d:
                d.Velocity = s.Velocity; d.Timbre = s.Timbre; d.Pressure = s.Pressure; d.Source = s.Source; break;
            case VoiceControlModulator s when to is VoiceControlModulator d:
                d.VoiceIndex = s.VoiceIndex; d.VoiceCount = s.VoiceCount; break;
            case VibratoModulator s when to is VibratoModulator d:
                d.Rate = s.Rate; d.Depth = s.Depth; break;
            case StackSpreadModulator s when to is StackSpreadModulator d:
                d.Spread = s.Spread; d.Rate = s.Rate; break;
            case XyModulator s when to is XyModulator d:
                d.X = s.X; d.Y = s.Y; break;
            case GlobalsModulator s when to is GlobalsModulator d:
                d.Source = s.Source; break;
            case AudioRateModulator s when to is AudioRateModulator d:
                d.Rate = s.Rate; break;
            case AudioSidechainModulator s when to is AudioSidechainModulator d:
                d.Amount = s.Amount; break;
            case NoteSidechainModulator s when to is NoteSidechainModulator d:
                d.Decay = s.Decay; break;
            case NoteCounterModulator s when to is NoteCounterModulator d:
                d.Max = s.Max; break;
            case KeytrackPlusModulator s when to is KeytrackPlusModulator d:
                d.Root = s.Root; d.Range = s.Range; d.Note = s.Note; break;
            case Pitch12Modulator s when to is Pitch12Modulator d:
                d.Note = s.Note; break;
            case RelativeKeytrackModulator s when to is RelativeKeytrackModulator d:
                d.Center = s.Center; d.Note = s.Note; d.Range = s.Range; break;
            case MidiModulator s when to is MidiModulator d:
                d.Cc = s.Cc; d.Value = s.Value; break;
            case HwCvInModulator s when to is HwCvInModulator d:
                d.Input = s.Input; d.Value = s.Value; break;
            case Channel16Modulator s when to is Channel16Modulator d:
                d.Channel = s.Channel; break;
            case MathModulator s when to is MathModulator d:
                d.A = s.A; d.B = s.B; d.Op = s.Op; break;
            case MixModulator s when to is MixModulator d:
                d.A = s.A; d.B = s.B; d.Crossfade = s.Crossfade; break;
            case PolynomModulator s when to is PolynomModulator d:
                d.Input = s.Input; d.A = s.A; d.B = s.B; d.C = s.C; break;
            case QuantizeModulator s when to is QuantizeModulator d:
                d.Input = s.Input; d.Steps = s.Steps; break;
            case Select4Modulator s when to is Select4Modulator d:
                d.V0 = s.V0; d.V1 = s.V1; d.V2 = s.V2; d.V3 = s.V3; d.Select = s.Select; break;
            case Vector4Modulator s when to is Vector4Modulator d:
                d.X = s.X; d.Y = s.Y; d.Z0 = s.Z0; d.Z1 = s.Z1; break;
            case Vector8Modulator s when to is Vector8Modulator d:
                d.X = s.X; d.Y = s.Y; d.Corners = (double[])s.Corners.Clone(); break;
            case ParSeq8Modulator s when to is ParSeq8Modulator d:
                d.RateBeats = s.RateBeats; d.Steps = (double[])s.Steps.Clone(); break;
        }
    }
}
