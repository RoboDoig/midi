using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents an operator that creates a MIDI note event with the specified properties.
    /// </summary>
    /// <remarks>
    /// If the input is a sequence of boolean values, <see langword="true"/> emits a note on
    /// event and <see langword="false"/> emits a note off event for the most recent note on,
    /// even if the note number has changed in the meantime. For any other input, an event of
    /// the specified <see cref="EventType"/> is emitted for each notification.
    /// </remarks>
    [Description("Creates a MIDI note event with the specified properties.")]
    public class CreateNoteEvent : Source<MidiEvent>
    {
        /// <summary>
        /// Gets or sets the type of note event to create.
        /// </summary>
        [Description("The type of note event to create.")]
        public NoteEventType EventType { get; set; }

        /// <summary>
        /// Gets or sets the MIDI channel of the note event, from 0 to 15.
        /// </summary>
        [Range(0, 15)]
        [Description("The MIDI channel of the note event, from 0 to 15.")]
        public int Channel { get; set; }

        /// <summary>
        /// Gets or sets the MIDI note number, from 0 to 127, where middle C is 60.
        /// </summary>
        [Range(0, 127)]
        [Description("The MIDI note number, from 0 to 127, where middle C is 60.")]
        public int NoteNumber { get; set; } = 60;

        /// <summary>
        /// Gets or sets the note velocity, from 0 to 127.
        /// </summary>
        [Range(0, 127)]
        [Description("The note velocity, from 0 to 127.")]
        public int Velocity { get; set; } = 100;

        /// <summary>
        /// Generates an observable sequence that contains a single MIDI note event.
        /// </summary>
        /// <returns>A sequence containing a single <see cref="MidiEvent"/> object.</returns>
        public override IObservable<MidiEvent> Generate()
        {
            return Observable.Defer(() => Observable.Return(CreateEvent(EventType, NoteNumber)));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI note events, where a new event is
        /// emitted whenever the source sequence emits a notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">The sequence containing the notifications used to emit new note events.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate<TSource>(IObservable<TSource> source)
        {
            return source.Select(_ => CreateEvent(EventType, NoteNumber));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI note events, where each <see langword="true"/>
        /// value emits a note on event and each <see langword="false"/> value emits a note off
        /// event for the most recent note on.
        /// </summary>
        /// <param name="source">A sequence of boolean values specifying whether the note is active.</param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects.</returns>
        public IObservable<MidiEvent> Generate(IObservable<bool> source)
        {
            return Observable.Create<MidiEvent>(observer =>
            {
                int? activeNote = null;
                return source.Subscribe(value =>
                {
                    if (activeNote.HasValue)
                    {
                        // Release the active note first, in case of repeated note on values
                        observer.OnNext(CreateEvent(NoteEventType.NoteOff, activeNote.Value));
                        activeNote = null;
                    }

                    if (value)
                    {
                        var noteNumber = NoteNumber;
                        observer.OnNext(CreateEvent(NoteEventType.NoteOn, noteNumber));
                        activeNote = noteNumber;
                    }
                },
                observer.OnError,
                observer.OnCompleted);
            });
        }

        MidiEvent CreateEvent(NoteEventType eventType, int noteNumber)
        {
            var note = MidiValue.ToSevenBitNumber(noteNumber, nameof(NoteNumber));
            var channel = MidiValue.ToFourBitNumber(Channel, nameof(Channel));
            return eventType == NoteEventType.NoteOff
                ? new NoteOffEvent(note, SevenBitNumber.MinValue) { Channel = channel }
                : new NoteOnEvent(note, MidiValue.ToSevenBitNumber(Velocity, nameof(Velocity))) { Channel = channel };
        }
    }
}
