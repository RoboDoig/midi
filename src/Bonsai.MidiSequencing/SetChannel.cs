using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that sends all channel events in the sequence on the specified MIDI channel.
    /// </summary>
    /// <remarks>
    /// Note off and aftertouch events are always sent on the same channel as the note on
    /// event which started the note, so changing the channel while notes are held does not
    /// leave hanging notes. Events which are not channel events pass through unchanged.
    /// </remarks>
    [Description("Sends all channel events in the sequence on the specified MIDI channel.")]
    public class SetChannel : Transform<MidiEvent, MidiEvent>
    {
        /// <summary>
        /// Gets or sets the MIDI channel, from 0 to 15, on which to send all channel events.
        /// </summary>
        [Range(0, 15)]
        [Description("The MIDI channel, from 0 to 15, on which to send all channel events.")]
        public int Channel { get; set; }

        /// <summary>
        /// Sends all channel events in an observable sequence on the specified MIDI channel.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiEvent"/> objects.</param>
        /// <returns>
        /// A sequence of <see cref="MidiEvent"/> objects where all channel events have
        /// been assigned to the specified channel.
        /// </returns>
        public override IObservable<MidiEvent> Process(IObservable<MidiEvent> source)
        {
            return Observable.Defer(() =>
            {
                // Maps each held note to the channel it was sent on
                var activeNotes = new Dictionary<(FourBitNumber, SevenBitNumber), FourBitNumber>();

                FourBitNumber Release(FourBitNumber sourceChannel, SevenBitNumber noteNumber, bool remove)
                {
                    var key = (sourceChannel, noteNumber);
                    if (activeNotes.TryGetValue(key, out var target))
                    {
                        if (remove) activeNotes.Remove(key);
                        return target;
                    }

                    return MidiValue.ToFourBitNumber(Channel, nameof(Channel));
                }

                return source.Select(midiEvent =>
                {
                    if (midiEvent is not ChannelEvent channelEvent)
                        return midiEvent;

                    var target = channelEvent switch
                    {
                        NoteOnEvent noteOn when noteOn.Velocity > 0 =>
                            activeNotes[(noteOn.Channel, noteOn.NoteNumber)] = MidiValue.ToFourBitNumber(Channel, nameof(Channel)),
                        NoteEvent noteEvent => Release(noteEvent.Channel, noteEvent.NoteNumber, remove: true),
                        NoteAftertouchEvent aftertouch => Release(aftertouch.Channel, aftertouch.NoteNumber, remove: false),
                        _ => MidiValue.ToFourBitNumber(Channel, nameof(Channel))
                    };

                    if (target == channelEvent.Channel)
                        return midiEvent;

                    // Clone the event since sequences may be shared between players
                    var remapped = (ChannelEvent)channelEvent.Clone();
                    remapped.Channel = target;
                    return remapped;
                });
            });
        }
    }
}
