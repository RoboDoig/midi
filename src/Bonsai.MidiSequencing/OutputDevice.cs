using Melanchall.DryWetMidi.Core;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that sends all MIDI events in the sequence to an output device.
    /// </summary>
    [Description("Sends all MIDI events in the sequence to an output device.")]
    public class OutputDevice : Sink<MidiEvent>
    {
        /// <summary>
        /// Gets or sets the index of the MIDI output device to connect to.
        /// </summary>
        public int? Index { get; set; }

        /// <summary>
        /// Sends all MIDI events in an observable sequence to the specified output device.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiEvent"/> objects to send.</param>
        /// <returns>
        /// An observable sequence that is identical to the <paramref name="source"/>
        /// sequence but where there is an additional side effect of sending each
        /// MIDI event to the output device.
        /// </returns>
        public override IObservable<MidiEvent> Process(IObservable<MidiEvent> source)
        {
            return Observable.Create<MidiEvent>(observer =>
            {
                var device = Melanchall.DryWetMidi.Multimedia.OutputDevice.GetByIndex(Index.GetValueOrDefault());
                device.PrepareForEventsSending();
                var subscription = source.Do(device.SendEvent).SubscribeSafe(observer);
                return Disposable.Create(() =>
                {
                    subscription.Dispose();
                    device.TurnAllNotesOff();
                    device.Dispose();
                });
            });
        }
    }
}
