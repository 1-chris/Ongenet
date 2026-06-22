using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ongenet.Audio.Interop;
using Ongenet.Core.Audio;

namespace Ongenet.Audio.Native;

/// <summary>
/// ALSA PCM driver for the native Linux backend. Enumerates the machine's PCM devices via ALSA's hint
/// API (tagging each "ALSA" with an "alsa:" id) and opens float32-interleaved streams on them through
/// <see cref="AlsaStream"/>. On a PipeWire/PulseAudio desktop the <c>default</c>/<c>pipewire</c>/
/// <c>pulse</c> PCMs route through those servers, so this one driver already replaces PortAudio on the
/// large majority of Linux systems.
/// </summary>
internal sealed class AlsaAudioDriver : INativeAudioDriver
{
    public string HostApi => "ALSA";
    public string IdPrefix => "alsa:";

    private bool? _available;
    public bool IsAvailable => _available ??= AlsaPcmNative.TryProbe();

    public void Enumerate(List<AudioDevice> outputs, List<AudioDevice> inputs)
    {
        if (!IsAvailable) return;
        if (AlsaPcmNative.snd_device_name_hint(-1, "pcm", out var hints) < 0 || hints == IntPtr.Zero) return;

        // Collect candidates keyed by final PCM name so the several hints that map to one card
        // (sysdefault:/hw:/plughw: for the same CARD=) collapse into a single entry with merged direction.
        var map = new Dictionary<string, Candidate>();
        var order = new List<string>();

        try
        {
            for (var p = hints; ; p += IntPtr.Size)
            {
                var entry = Marshal.ReadIntPtr(p);
                if (entry == IntPtr.Zero) break;

                var name = AlsaPcmNative.ReadHint(entry, "NAME");
                if (string.IsNullOrEmpty(name) || name == "null") continue;

                var desc = AlsaPcmNative.ReadHint(entry, "DESC");
                var ioid = AlsaPcmNative.ReadHint(entry, "IOID"); // "Input" | "Output" | null (both)
                var canOut = ioid != "Input";
                var canIn = ioid != "Output";

                string pcm, label;
                var isDefault = false;
                if (IsServerPcm(name))
                {
                    // The server/routing PCMs respect the system's default sink — but on some setups
                    // (e.g. an EasyEffects virtual sink that isn't forwarding) they route to a dead end,
                    // so we also expose the hardware directly below.
                    pcm = name;
                    label = ServerLabel(name);
                    isDefault = name == "default";
                }
                else if (TryCardId(name, out var cardId))
                {
                    // A real sound card: open it through the format-converting `plug` layer, which grabs
                    // the hardware directly (bypassing any server routing — this is what actually makes
                    // sound on a box whose default PCM dead-ends) and accepts float32 at any rate.
                    pcm = $"plughw:CARD={cardId}";
                    label = CardLabel(desc, cardId);
                }
                else
                {
                    continue; // skip raw/duplicate hints we don't surface
                }

                if (map.TryGetValue(pcm, out var c))
                {
                    map[pcm] = c with { CanOut = c.CanOut || canOut, CanIn = c.CanIn || canIn, IsDefault = c.IsDefault || isDefault };
                }
                else
                {
                    map[pcm] = new Candidate(label, canOut, canIn, isDefault);
                    order.Add(pcm);
                }
            }
        }
        finally
        {
            AlsaPcmNative.snd_device_name_free_hint(hints);
        }

        var index = 0;
        foreach (var pcm in order)
        {
            var c = map[pcm];
            var device = new AudioDevice(
                Index: index++,
                Name: c.Label,
                HostApi: HostApi,
                MaxInputChannels: c.CanIn ? 2 : 0,
                MaxOutputChannels: c.CanOut ? 2 : 0,
                IsDefaultInput: c.IsDefault,
                IsDefaultOutput: c.IsDefault,
                Id: IdPrefix + pcm);

            if (device.SupportsOutput) outputs.Add(device);
            if (device.SupportsInput) inputs.Add(device);
        }
    }

    public INativeStream OpenOutput(AudioDevice device, int channels, AudioRenderCallback render)
        => AlsaStream.Open(PcmName(device), playback: true, channels, render, null);

    public INativeStream OpenInput(AudioDevice device, int channels, AudioCaptureCallback capture)
        => AlsaStream.Open(PcmName(device), playback: false, channels, null, capture);

    // The raw ALSA PCM name behind our tagged id (falls back to the display name for hand-built devices).
    private string PcmName(AudioDevice device)
        => device.Id.StartsWith(IdPrefix, StringComparison.Ordinal) ? device.Id[IdPrefix.Length..] : device.Name;

    private static bool IsServerPcm(string name) => name is "default" or "pipewire" or "pulse" or "jack";

    // Pulls the card id out of a "…:CARD=<id>[,…]" PCM name (e.g. "sysdefault:CARD=DX1" → "DX1").
    private static bool TryCardId(string name, out string cardId)
    {
        cardId = "";
        var m = Regex.Match(name, @"CARD=([^,\]]+)");
        if (!m.Success) return false;
        cardId = m.Groups[1].Value;
        return true;
    }

    private static string ServerLabel(string name) => name switch
    {
        "default" => "System default (PipeWire/Pulse)",
        "pipewire" => "PipeWire (system mixer)",
        "pulse" => "PulseAudio (system mixer)",
        "jack" => "JACK",
        _ => name,
    };

    // First line of the ALSA DESC is the friendly card name (e.g. "DX1, USB Audio"); fall back to the id.
    private static string CardLabel(string? desc, string cardId)
    {
        var firstLine = desc?.Split('\n', 2)[0].Trim();
        return string.IsNullOrEmpty(firstLine) ? cardId : firstLine;
    }

    private readonly record struct Candidate(string Label, bool CanOut, bool CanIn, bool IsDefault);
}
