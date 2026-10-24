using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that shifts the pitch of all note events in the sequence
    /// by the specified number of semitones.
    /// </summary>
    /// <remarks>
    /// Note off and aftertouch events are always shifted by the same amount as the note on
    /// event which started the note, so changing the transposition while notes are held
    /// does not leave hanging notes. Notes shifted outside the valid MIDI note range are
    /// dropped. All other events pass through unchanged.
    /// </remarks>
    [Description("Shifts the pitch of all note events in the sequence by the specified number of semitones.")]
    public class TransposeNote : Transform<MidiEvent, MidiEvent>
    {
        /// <summary>
        /// Gets or sets the number of semitones by which to shift the pitch of each note.
        /// </summary>
        [Description("The number of semitones by which to shift the pitch of each note.")]
        public int Semitones { get; set; }

        /// <summary>
        /// Shifts the pitch of all note events in an observable sequence by the
        /// specified number of semitones.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiEvent"/> objects.</param>
        /// <returns>
        /// A sequence of <see cref="MidiEvent"/> objects where all note events have
        /// been transposed.
        /// </returns>
        public override IObservable<MidiEvent> Process(IObservable<MidiEvent> source)
        {
            return Observable.Defer(() =>
            {
                // Maps each held note to the transposed note number it was sent as,
                // or null if it was dropped for being out of range
                var activeNotes = new Dictionary<(FourBitNumber, SevenBitNumber), SevenBitNumber?>();

                SevenBitNumber? Shift(SevenBitNumber noteNumber)
                {
                    var shifted = noteNumber + Semitones;
                    return shifted >= SevenBitNumber.MinValue && shifted <= SevenBitNumber.MaxValue
                        ? (SevenBitNumber)(byte)shifted
                        : null;
                }

                SevenBitNumber? Release(FourBitNumber channel, SevenBitNumber noteNumber)
                {
                    var key = (channel, noteNumber);
                    if (activeNotes.TryGetValue(key, out var target))
                    {
                        activeNotes.Remove(key);
                        return target;
                    }

                    return Shift(noteNumber);
                }

                MidiEvent? Transpose(MidiEvent midiEvent)
                {
                    switch (midiEvent)
                    {
                        case NoteOnEvent noteOn when noteOn.Velocity > 0:
                            var onTarget = Shift(noteOn.NoteNumber);
                            activeNotes[(noteOn.Channel, noteOn.NoteNumber)] = onTarget;
                            return WithNoteNumber(noteOn, onTarget);
                        case NoteOnEvent noteOn:
                            return WithNoteNumber(noteOn, Release(noteOn.Channel, noteOn.NoteNumber));
                        case NoteOffEvent noteOff:
                            return WithNoteNumber(noteOff, Release(noteOff.Channel, noteOff.NoteNumber));
                        case NoteAftertouchEvent aftertouch:
                            var aftertouchTarget = activeNotes.TryGetValue((aftertouch.Channel, aftertouch.NoteNumber), out var heldTarget)
                                ? heldTarget
                                : Shift(aftertouch.NoteNumber);
                            if (!aftertouchTarget.HasValue) return null;
                            var transposedAftertouch = (NoteAftertouchEvent)aftertouch.Clone();
                            transposedAftertouch.NoteNumber = aftertouchTarget.Value;
                            return transposedAftertouch;
                        default:
                            return midiEvent;
                    }
                }

                return source
                    .Select(Transpose)
                    .Where(midiEvent => midiEvent != null)
                    .Select(midiEvent => midiEvent!);
            });
        }

        static MidiEvent? WithNoteNumber(NoteEvent noteEvent, SevenBitNumber? noteNumber)
        {
            if (!noteNumber.HasValue)
                return null;

            // Clone the event since sequences may be shared between players
            var transposed = (NoteEvent)noteEvent.Clone();
            transposed.NoteNumber = noteNumber.Value;
            return transposed;
        }
    }
}
