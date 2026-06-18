using System.Threading;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// A lock-free record of recently-spawned grains, for UI visualisation. The audio thread reports each
/// grain as it is spawned (single producer); the UI drains new entries on its own timer (single
/// consumer) and animates them. Purely cosmetic — dropping or missing entries has no audio effect.
/// </summary>
public sealed class GrainMonitor
{
    /// <summary>One spawned grain: where in the sample it came from, its pan, length, and direction.</summary>
    public readonly struct Blip
    {
        public Blip(float position, float pan, float durationSeconds, bool reverse)
        {
            Position = position;
            Pan = pan;
            DurationSeconds = durationSeconds;
            Reverse = reverse;
        }

        /// <summary>Normalised position in the source sample, 0..1.</summary>
        public float Position { get; }

        /// <summary>Pan placement, -1 (left) .. +1 (right).</summary>
        public float Pan { get; }

        /// <summary>Grain length in seconds (drives the fade animation).</summary>
        public float DurationSeconds { get; }

        /// <summary>Whether the grain plays in reverse.</summary>
        public bool Reverse { get; }
    }

    private const int Size = 512; // power of two ring
    private readonly Blip[] _ring = new Blip[Size];
    private long _write;

    /// <summary>Total grains reported so far (monotonic). The UI compares against its own last cursor.</summary>
    public long Cursor => Volatile.Read(ref _write);

    /// <summary>Audio thread: records a spawned grain.</summary>
    public void Report(float position, float pan, float durationSeconds, bool reverse)
    {
        _ring[(int)(_write & (Size - 1))] = new Blip(position, pan, durationSeconds, reverse);
        Volatile.Write(ref _write, _write + 1);
    }

    /// <summary>UI thread: reads the entry at <paramref name="index"/> if it hasn't yet been overwritten.</summary>
    public bool TryGet(long index, out Blip blip)
    {
        var cursor = Cursor;
        if (index < 0 || index >= cursor || cursor - index > Size)
        {
            blip = default;
            return false;
        }

        blip = _ring[(int)(index & (Size - 1))];
        return true;
    }
}
