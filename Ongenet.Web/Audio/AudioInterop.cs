using System.Runtime.InteropServices.JavaScript;

namespace Ongenet.Web.Audio;

/// <summary>
/// Thin bridge to <c>wwwroot/ongen-audio.js</c>, which owns the browser's <c>AudioContext</c> and a
/// <c>ScriptProcessorNode</c>. The node pulls audio on the main thread by calling the exported
/// <see cref="RenderBlock"/> (wired to <c>globalThis.ongenAudioRender</c> in <c>main.js</c>); each call
/// returns one interleaved block.
///
/// <para>A <c>ScriptProcessorNode</c> (rather than an <c>AudioWorklet</c> + <c>SharedArrayBuffer</c> ring
/// buffer) is used deliberately: it needs no cross-origin isolation, so the app runs from plain static
/// hosting (GitHub Pages) with no COOP/COEP headers. The cost is main-thread rendering — audio can glitch
/// while the UI is busy — which is acceptable for a demo. The worklet path is the future performance
/// upgrade if real headers become available.</para>
/// </summary>
internal static partial class AudioInterop
{
    /// <summary>Creates the AudioContext + node for <paramref name="channels"/> channels; returns the
    /// context's actual sample rate (often 48000), or 0 if audio could not start.</summary>
    [JSImport("startAudio", "ongen-audio")]
    public static partial int StartAudio(int channels);

    /// <summary>Tears down the audio graph.</summary>
    [JSImport("stopAudio", "ongen-audio")]
    public static partial void StopAudio();

    /// <summary>
    /// Pulled by JS once per audio block (via <c>globalThis.ongenAudioRender</c>). Returns
    /// <paramref name="frames"/> × <paramref name="channels"/> interleaved samples as <c>double[]</c>,
    /// which marshals cleanly to a JS number array.
    /// </summary>
    [JSExport]
    internal static double[] RenderBlock(int frames, int channels)
        => WebAudioOutput.Active?.Render(frames, channels) ?? new double[frames * channels];
}
