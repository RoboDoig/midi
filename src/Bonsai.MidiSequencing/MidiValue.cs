using System;
using Melanchall.DryWetMidi.Common;

namespace Bonsai.MidiSequencing
{
    static class MidiValue
    {
        public static SevenBitNumber ToSevenBitNumber(int value, string name)
        {
            if (value < SevenBitNumber.MinValue || value > SevenBitNumber.MaxValue)
            {
                throw new InvalidOperationException($"{name} must be between {SevenBitNumber.MinValue} and {SevenBitNumber.MaxValue}.");
            }

            return (SevenBitNumber)(byte)value;
        }

        public static FourBitNumber ToFourBitNumber(int value, string name)
        {
            if (value < FourBitNumber.MinValue || value > FourBitNumber.MaxValue)
            {
                throw new InvalidOperationException($"{name} must be between {FourBitNumber.MinValue} and {FourBitNumber.MaxValue}.");
            }

            return (FourBitNumber)(byte)value;
        }
    }
}
