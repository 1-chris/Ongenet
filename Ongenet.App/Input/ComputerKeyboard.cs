using System.Collections.Generic;
using Avalonia.Input;

namespace Ongenet.App.Input
{
    /// <summary>
    /// Maps the typing keyboard to piano notes using FL Studio's layout: the lower letter rows are
    /// the lower octave (Z=C, S=C#, X=D, …) and the upper letter/number rows are one octave above
    /// (Q=C, 2=C#, W=D, …), giving the two overlapping octaves FL provides.
    /// </summary>
    public static class ComputerKeyboard
    {
        /// <summary>MIDI note for the lowest mapped key (Z). Middle C.</summary>
        public const int BaseNote = 60;

        private static readonly Dictionary<Key, int> Offsets = new()
        {
            // Lower octave — Z row (white) + S/D/G/H/J/L (black) + , . ; / into the next octave.
            { Key.Z, 0 }, { Key.S, 1 }, { Key.X, 2 }, { Key.D, 3 }, { Key.C, 4 }, { Key.V, 5 },
            { Key.G, 6 }, { Key.B, 7 }, { Key.H, 8 }, { Key.N, 9 }, { Key.J, 10 }, { Key.M, 11 },
            { Key.OemComma, 12 }, { Key.L, 13 }, { Key.OemPeriod, 14 }, { Key.OemSemicolon, 15 }, { Key.OemQuestion, 16 },

            // Upper octave — Q row (white) + 2/3/5/6/7/9/0 (black), one octave above the Z row.
            { Key.Q, 12 }, { Key.D2, 13 }, { Key.W, 14 }, { Key.D3, 15 }, { Key.E, 16 }, { Key.R, 17 },
            { Key.D5, 18 }, { Key.T, 19 }, { Key.D6, 20 }, { Key.Y, 21 }, { Key.D7, 22 }, { Key.U, 23 },
            { Key.I, 24 }, { Key.D9, 25 }, { Key.O, 26 }, { Key.D0, 27 }, { Key.P, 28 },
            { Key.OemOpenBrackets, 29 }, { Key.OemPlus, 30 }, { Key.OemCloseBrackets, 31 }
        };

        /// <summary>Maps a key to a MIDI note; returns false for unmapped keys.</summary>
        public static bool TryGetNote(Key key, out int note)
        {
            if (Offsets.TryGetValue(key, out var offset))
            {
                note = BaseNote + offset;
                return true;
            }

            note = 0;
            return false;
        }
    }
}
