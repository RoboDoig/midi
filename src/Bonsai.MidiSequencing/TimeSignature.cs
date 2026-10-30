using System;
using System.ComponentModel;
using System.Globalization;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents the time signature of a MIDI sequence, specifying the number of beats
    /// in each bar and the note value of each beat.
    /// </summary>
    [TypeConverter(typeof(TimeSignatureConverter))]
    public class TimeSignature
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TimeSignature"/> class
        /// with a common time of 4/4.
        /// </summary>
        public TimeSignature()
            : this(4, 4)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeSignature"/> class
        /// with the specified number of beats per bar and beat unit.
        /// </summary>
        /// <param name="beatsPerBar">The number of beats in each bar, i.e. the upper number of the time signature.</param>
        /// <param name="beatUnit">The note value of each beat, i.e. the lower number of the time signature.</param>
        public TimeSignature(int beatsPerBar, int beatUnit)
        {
            BeatsPerBar = beatsPerBar;
            BeatUnit = beatUnit;
        }

        /// <summary>
        /// Gets or sets the number of beats in each bar, i.e. the upper number of the time signature.
        /// </summary>
        [Description("The number of beats in each bar, i.e. the upper number of the time signature.")]
        public int BeatsPerBar { get; set; }

        /// <summary>
        /// Gets or sets the note value of each beat, i.e. the lower number of the time signature,
        /// where 4 is a quarter note and 8 is an eighth note.
        /// </summary>
        [Description("The note value of each beat, i.e. the lower number of the time signature, where 4 is a quarter note and 8 is an eighth note.")]
        public int BeatUnit { get; set; }

        /// <summary>
        /// Gets the length of each bar, in quarter note beats.
        /// </summary>
        [Browsable(false)]
        public double BarLength => BeatsPerBar * 4.0 / BeatUnit;

        /// <summary>
        /// Creates a copy of this time signature.
        /// </summary>
        /// <returns>A new <see cref="TimeSignature"/> object with the same values.</returns>
        public TimeSignature Clone()
        {
            return new TimeSignature(BeatsPerBar, BeatUnit);
        }

        /// <summary>
        /// Throws an exception if the time signature does not have a positive number of beats per bar
        /// and a beat unit which is a power of two.
        /// </summary>
        /// <exception cref="InvalidOperationException">The time signature is not valid.</exception>
        public void Validate()
        {
            if (BeatsPerBar <= 0)
            {
                throw new InvalidOperationException("The number of beats per bar must be a positive number.");
            }

            if (BeatUnit <= 0 || (BeatUnit & (BeatUnit - 1)) != 0)
            {
                throw new InvalidOperationException("The beat unit must be a power of two, e.g. 2, 4 or 8.");
            }
        }

        /// <summary>
        /// Converts the string representation of a time signature, e.g. <c>6/8</c>, to
        /// an equivalent <see cref="TimeSignature"/> object.
        /// </summary>
        /// <param name="text">A string containing the time signature to convert.</param>
        /// <returns>A <see cref="TimeSignature"/> object equivalent to the specified string.</returns>
        public static TimeSignature Parse(string text)
        {
            var parts = (text ?? string.Empty).Split('/');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatsPerBar) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatUnit))
            {
                throw new FormatException($"Invalid time signature '{text}'. Time signatures must be written as two numbers separated by a slash, e.g. 6/8.");
            }

            return new TimeSignature(beatsPerBar, beatUnit);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{BeatsPerBar}/{BeatUnit}";
        }
    }

    class TimeSignatureConverter : ExpandableObjectConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string text)
            {
                // An empty string clears optional time signatures in the property grid
                return string.IsNullOrWhiteSpace(text) ? null! : TimeSignature.Parse(text);
            }

            return base.ConvertFrom(context, culture, value);
        }
    }
}
