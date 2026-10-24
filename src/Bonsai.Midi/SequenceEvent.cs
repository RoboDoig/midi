using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents a MIDI event scheduled at a musical position within a sequence.
    /// </summary>
    public readonly struct SequenceEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceEvent"/> structure.
        /// </summary>
        /// <param name="beat">The position of the event from the start of the sequence, in beats.</param>
        /// <param name="midiEvent">The MIDI event to emit.</param>
        public SequenceEvent(double beat, MidiEvent midiEvent)
            : this(beat, midiEvent, track: 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceEvent"/> structure.
        /// </summary>
        /// <param name="beat">The position of the event from the start of the sequence, in beats.</param>
        /// <param name="midiEvent">The MIDI event to emit.</param>
        /// <param name="track">The zero-based index of the track the event belongs to.</param>
        public SequenceEvent(double beat, MidiEvent midiEvent, int track)
        {
            Beat = beat;
            Event = midiEvent;
            Track = track;
        }

        /// <summary>
        /// Gets the position of the event from the start of the sequence, in beats.
        /// </summary>
        public double Beat { get; }

        /// <summary>
        /// Gets the MIDI event to emit.
        /// </summary>
        public MidiEvent Event { get; }

        /// <summary>
        /// Gets the zero-based index of the track the event belongs to.
        /// </summary>
        public int Track { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Beat:F3}: Track {Track}: {Event}";
        }
    }
}
