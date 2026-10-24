using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents a MIDI event emitted during sequence playback, together with the index
    /// of the track it belongs to.
    /// </summary>
    /// <remarks>
    /// A track event is implicitly convertible to <see cref="MidiEvent"/>, so it can be
    /// passed directly to any operator accepting MIDI events.
    /// </remarks>
    public class TrackEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TrackEvent"/> class.
        /// </summary>
        /// <param name="track">The zero-based index of the track the event belongs to.</param>
        /// <param name="midiEvent">The MIDI event.</param>
        public TrackEvent(int track, MidiEvent midiEvent)
        {
            Track = track;
            Event = midiEvent;
        }

        /// <summary>
        /// Gets the zero-based index of the track the event belongs to.
        /// </summary>
        public int Track { get; }

        /// <summary>
        /// Gets the MIDI event.
        /// </summary>
        public MidiEvent Event { get; }

        /// <summary>
        /// Converts a track event to the MIDI event it contains.
        /// </summary>
        /// <param name="trackEvent">The track event to convert.</param>
        public static implicit operator MidiEvent(TrackEvent trackEvent)
        {
            return trackEvent.Event;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Track {Track}: {Event}";
        }
    }
}
