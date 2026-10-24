using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents a sequence of MIDI events in musical time, with an explicit length.
    /// </summary>
    public class MidiSequence
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MidiSequence"/> class.
        /// </summary>
        /// <param name="events">The events in the sequence.</param>
        /// <param name="length">
        /// The length of the sequence, in beats. All events must be scheduled within this length.
        /// </param>
        public MidiSequence(IEnumerable<SequenceEvent> events, double length)
        {
            if (events is null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "The sequence length must be non-negative.");
            }

            // Order note-offs before note-ons at the same position so that repeated
            // notes are not cut off by the release of the previous note
            Events = events
                .OrderBy(sequenceEvent => sequenceEvent.Beat)
                .ThenBy(sequenceEvent => sequenceEvent.Event is NoteOnEvent noteOn && noteOn.Velocity > 0 ? 1 : 0)
                .ToArray();
            if (Events.Any(sequenceEvent => sequenceEvent.Beat < 0 || sequenceEvent.Beat > length))
            {
                throw new ArgumentException("All events must be scheduled within the sequence length.", nameof(events));
            }

            Length = length;
        }

        /// <summary>
        /// Gets the events in the sequence, ordered by position.
        /// </summary>
        public IReadOnlyList<SequenceEvent> Events { get; }

        /// <summary>
        /// Gets the length of the sequence, in beats.
        /// </summary>
        public double Length { get; }

        /// <summary>
        /// Creates a sequence from all non-meta events in a standard MIDI file, measured in
        /// quarter notes. The length of the sequence is the position of the last event rounded
        /// up to a whole beat. The tempo map of the file is ignored, since tempo is defined
        /// by the transport. Each event is tagged with the zero-based index of its track chunk.
        /// </summary>
        /// <param name="file">The MIDI file to convert.</param>
        /// <returns>A new <see cref="MidiSequence"/> containing the events in the file.</returns>
        public static MidiSequence FromMidiFile(MidiFile file)
        {
            if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision timeDivision)
            {
                throw new NotSupportedException("Only MIDI files with ticks per quarter note time division are supported.");
            }

            // Tag each event with the index of its track chunk, so events can be filtered by track
            var ticksPerBeat = (double)timeDivision.TicksPerQuarterNote;
            var events = file.GetTrackChunks()
                .SelectMany((track, trackIndex) => track.GetTimedEvents()
                    .Where(timedEvent => timedEvent.Event is not MetaEvent)
                    .Select(timedEvent => new SequenceEvent(timedEvent.Time / ticksPerBeat, timedEvent.Event, trackIndex)))
                .ToArray();
            var length = events.Length > 0 ? Math.Ceiling(events.Max(sequenceEvent => sequenceEvent.Beat)) : 0;
            return new MidiSequence(events, length);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{{Events = {Events.Count}, Length = {Length}}}";
        }
    }
}
