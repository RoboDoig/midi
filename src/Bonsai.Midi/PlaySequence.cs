using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.Midi
{
    /// <summary>
    /// Represents an operator that plays back MIDI sequences, emitting each event
    /// when its scheduled beat is reached on the specified transport.
    /// </summary>
    /// <remarks>
    /// Playback timing is driven entirely by the transport sequence, and all events are
    /// emitted on the transport thread. Sequence players sharing the same transport are
    /// therefore always synchronized with each other, including across tempo changes.
    /// Receiving a new sequence schedules it to start on the next transport tick, or on
    /// the next multiple of <see cref="LaunchQuantization"/> beats, if specified. The
    /// current sequence keeps playing until the new sequence starts, at which point any
    /// active notes are released.
    /// </remarks>
    [Combinator]
    [WorkflowElementCategory(ElementCategory.Combinator)]
    [Description("Plays back MIDI sequences, emitting each event when its scheduled beat is reached on the specified transport.")]
    public class PlaySequence
    {
        /// <summary>
        /// Gets or sets the interval, in beats, to which the start of each sequence is
        /// aligned. If zero, sequences start on the next transport tick.
        /// </summary>
        [Description("The interval, in beats, to which the start of each sequence is aligned. If zero, sequences start on the next transport tick.")]
        public double LaunchQuantization { get; set; }

        /// <summary>
        /// Gets or sets a value specifying whether to loop the current sequence until
        /// a new sequence is received.
        /// </summary>
        [Description("Specifies whether to loop the current sequence until a new sequence is received.")]
        public bool Loop { get; set; }

        /// <summary>
        /// Plays back each MIDI file in an observable sequence, emitting its events
        /// as they are reached on the specified transport.
        /// </summary>
        /// <param name="source">
        /// A sequence of <see cref="MidiFile"/> objects to play. Event times are measured in
        /// quarter notes and the tempo map of each file is ignored.
        /// </param>
        /// <param name="transport">
        /// A sequence of <see cref="TransportState"/> values used to drive playback.
        /// </param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects in playback order.</returns>
        public IObservable<MidiEvent> Process(IObservable<MidiFile> source, IObservable<TransportState> transport)
        {
            return Process(source.Select(MidiSequence.FromMidiFile), transport);
        }

        /// <summary>
        /// Plays back each MIDI sequence in an observable sequence, emitting its events
        /// as they are reached on the specified transport.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiSequence"/> objects to play.</param>
        /// <param name="transport">
        /// A sequence of <see cref="TransportState"/> values used to drive playback.
        /// </param>
        /// <returns>A sequence of <see cref="MidiEvent"/> objects in playback order.</returns>
        public IObservable<MidiEvent> Process(IObservable<MidiSequence> source, IObservable<TransportState> transport)
        {
            return Observable.Create<MidiEvent>(observer =>
            {
                var gate = new object();
                var sourceCompleted = false;
                MidiSequence? pending = null;
                double? pendingStart = null;
                MidiSequence? current = null;
                var index = 0;
                var startBeat = 0.0;
                var activeNotes = new HashSet<(FourBitNumber, SevenBitNumber)>();
                var quantization = LaunchQuantization;

                void ReleaseActiveNotes()
                {
                    foreach (var (channel, noteNumber) in activeNotes)
                    {
                        observer.OnNext(new NoteOffEvent(noteNumber, SevenBitNumber.MinValue) { Channel = channel });
                    }
                    activeNotes.Clear();
                }

                void Emit(MidiEvent midiEvent)
                {
                    switch (midiEvent)
                    {
                        case NoteOnEvent noteOn when noteOn.Velocity > 0:
                            activeNotes.Add((noteOn.Channel, noteOn.NoteNumber));
                            break;
                        case NoteOnEvent noteOn:
                            activeNotes.Remove((noteOn.Channel, noteOn.NoteNumber));
                            break;
                        case NoteOffEvent noteOff:
                            activeNotes.Remove((noteOff.Channel, noteOff.NoteNumber));
                            break;
                    }
                    observer.OnNext(midiEvent);
                }

                // Emits all events of the current sequence scheduled up to the specified beat,
                // wrapping around the end of the sequence if looping is enabled
                void Advance(double untilBeat, bool inclusive)
                {
                    while (current != null)
                    {
                        var events = current.Events;
                        while (index < events.Count)
                        {
                            var eventBeat = startBeat + events[index].Beat;
                            if (inclusive ? eventBeat > untilBeat : eventBeat >= untilBeat) break;
                            Emit(events[index++].Event);
                        }

                        var endBeat = startBeat + current.Length;
                        if (untilBeat < endBeat) break;
                        if (Loop && current.Length > 0)
                        {
                            startBeat = endBeat;
                            index = 0;
                        }
                        else current = null;
                    }
                }

                var sourceObserver = Observer.Create<MidiSequence>(
                    sequence =>
                    {
                        lock (gate) pending = sequence;
                    },
                    error =>
                    {
                        lock (gate) observer.OnError(error);
                    },
                    () =>
                    {
                        lock (gate) sourceCompleted = true;
                    });

                var transportObserver = Observer.Create<TransportState>(
                    state =>
                    {
                        lock (gate)
                        {
                            var beat = state.Beat;
                            if (pending != null)
                            {
                                pendingStart ??= quantization > 0
                                    ? Math.Ceiling(beat / quantization) * quantization
                                    : beat;
                                if (beat >= pendingStart)
                                {
                                    Advance(pendingStart.Value, inclusive: false);
                                    ReleaseActiveNotes();
                                    current = pending;
                                    startBeat = pendingStart.Value;
                                    index = 0;
                                    pending = null;
                                    pendingStart = null;
                                }
                            }

                            Advance(beat, inclusive: true);
                            if (sourceCompleted && current == null && pending == null)
                            {
                                observer.OnCompleted();
                            }
                        }
                    },
                    error =>
                    {
                        lock (gate) observer.OnError(error);
                    },
                    () =>
                    {
                        lock (gate) observer.OnCompleted();
                    });

                return new CompositeDisposable(
                    transport.SubscribeSafe(transportObserver),
                    source.SubscribeSafe(sourceObserver));
            });
        }
    }
}
