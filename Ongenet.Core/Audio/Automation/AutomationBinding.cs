namespace Ongenet.Core.Audio.Automation;

/// <summary>What an automation lane drives, identified by position so it can be re-bound on load.</summary>
public enum AutomationTargetKind
{
    TrackVolume,
    TrackPan,
    EffectEnabled,
    EffectParam,
    InstrumentParam
}

/// <summary>
/// A serializable description of an automation lane's target, captured at creation time. Delegates can't be
/// persisted, so on load the project re-binds the lane by recreating the delegate target from this descriptor
/// against the loaded track's instrument/effects. <c>EffectIndex</c>/<c>ParamIndex</c> are -1 when unused.
/// </summary>
public sealed record AutomationBinding(AutomationTargetKind Kind, int EffectIndex, int ParamIndex);
