using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that filters the sequence for events belonging to the specified track.
    /// </summary>
    /// <remarks>
    /// In multi-track (type 1) MIDI files, the first track usually contains only tempo and
    /// time signature information, so instrument tracks usually start at index 1. Sequences
    /// created with <see cref="CreateNoteSequence"/> contain a single track with index 0.
    /// </remarks>
    [Description("Filters the sequence for events belonging to the specified track.")]
    public class FilterTrack : Transform<TrackEvent, TrackEvent>
    {
        /// <summary>
        /// Gets or sets the zero-based index of the track to keep.
        /// </summary>
        [Description("The zero-based index of the track to keep.")]
        public int Index { get; set; }

        /// <summary>
        /// Filters an observable sequence of track events, keeping only the events
        /// belonging to the specified track.
        /// </summary>
        /// <param name="source">A sequence of <see cref="TrackEvent"/> objects.</param>
        /// <returns>
        /// A sequence of <see cref="TrackEvent"/> objects belonging to the specified track.
        /// </returns>
        public override IObservable<TrackEvent> Process(IObservable<TrackEvent> source)
        {
            return source.Where(trackEvent => trackEvent.Track == Index);
        }
    }
}
