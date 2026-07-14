// WebAudio glue for the Ongenet browser demo. Owns a single AudioContext + ScriptProcessorNode that
// pulls interleaved float blocks from managed code (the engine's render callback) on the main thread.
//
// A ScriptProcessorNode is used on purpose: unlike an AudioWorklet + SharedArrayBuffer ring buffer, it
// needs no cross-origin isolation, so the app runs from plain static hosting (GitHub Pages) with no
// COOP/COEP headers. The tradeoff is main-thread rendering (can glitch when the UI is busy) — fine for
// a demo. See Ongenet.Web/Audio/AudioInterop.cs.

let ctx = null;
let node = null;
let channels = 2;

// AudioContexts start "suspended" until a user gesture. Resume on the first interaction.
function installResumeOnGesture() {
    const resume = () => {
        if (ctx && ctx.state === 'suspended') ctx.resume();
        window.removeEventListener('pointerdown', resume);
        window.removeEventListener('keydown', resume);
        window.removeEventListener('touchstart', resume);
    };
    window.addEventListener('pointerdown', resume);
    window.addEventListener('keydown', resume);
    window.addEventListener('touchstart', resume);
}

export function startAudio(ch) {
    channels = ch | 0;
    try {
        const AudioCtx = window.AudioContext || window.webkitAudioContext;
        ctx = new AudioCtx();

        // 8192 frames (~171ms @ 48kHz, ~6 Hz) — large demo buffer so WASM DSP gets long stretches
        // between callbacks while Avalonia paints on the same thread.
        const bufferSize = 8192;
        node = ctx.createScriptProcessor(bufferSize, 0, channels);
        node.onaudioprocess = (e) => {
            const out = e.outputBuffer;
            const frames = out.length;
            let data = null;
            const render = globalThis.ongenAudioRender;
            try { data = render ? render(frames, channels) : null; } catch (_) { data = null; }

            if (!data) {
                for (let c = 0; c < channels; c++) out.getChannelData(c).fill(0);
                return;
            }

            // Specialized stereo de-interleave (hot path); general loop for odd channel counts.
            if (channels === 2) {
                const L = out.getChannelData(0);
                const R = out.getChannelData(1);
                for (let i = 0, s = 0; i < frames; i++, s += 2) {
                    L[i] = data[s];
                    R[i] = data[s + 1];
                }
                return;
            }

            for (let c = 0; c < channels; c++) {
                const cd = out.getChannelData(c);
                for (let i = 0; i < frames; i++) cd[i] = data[i * channels + c];
            }
        };
        node.connect(ctx.destination);
        installResumeOnGesture();
        return ctx.sampleRate | 0;
    } catch (err) {
        console.error('startAudio failed', err);
        return 0;
    }
}

export function stopAudio() {
    try { if (node) node.disconnect(); } catch (_) {}
    try { if (ctx) ctx.close(); } catch (_) {}
    node = null;
    ctx = null;
}
