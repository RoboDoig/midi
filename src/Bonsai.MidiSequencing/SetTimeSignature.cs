using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that assigns a time signature to each MIDI sequence in the sequence.
    /// </summary>
    /// <remarks>
    /// The time signature can be specified using the <see cref="TimeSignature"/> property, or
    /// paired with each MIDI sequence in the input, e.g. using the <c>WithLatestFrom</c> operator.
    /// </remarks>
    [Description("Assigns a time signature to each MIDI sequence in the sequence.")]
    public class SetTimeSignature : Transform<MidiSequence, MidiSequence>
    {
        /// <summary>
        /// Gets or sets the time signature to assign to each MIDI sequence. If no value
        /// is specified, the time signature is removed.
        /// </summary>
        [Description("The time signature to assign to each MIDI sequence. If no value is specified, the time signature is removed.")]
        public TimeSignature? TimeSignature { get; set; }

        /// <summary>
        /// Assigns the time signature specified by the <see cref="TimeSignature"/> property
        /// to each MIDI sequence in an observable sequence.
        /// </summary>
        /// <param name="source">A sequence of <see cref="MidiSequence"/> objects.</param>
        /// <returns>A sequence of <see cref="MidiSequence"/> objects with the specified time signature.</returns>
        public override IObservable<MidiSequence> Process(IObservable<MidiSequence> source)
        {
            return source.Select(sequence => Assign(sequence, TimeSignature));
        }

        /// <summary>
        /// Assigns the paired time signature to each MIDI sequence in an observable sequence.
        /// </summary>
        /// <param name="source">
        /// A sequence of pairs containing each <see cref="MidiSequence"/> object and the
        /// <see cref="MidiSequencing.TimeSignature"/> to assign to it.
        /// </param>
        /// <returns>A sequence of <see cref="MidiSequence"/> objects with the paired time signature.</returns>
        public IObservable<MidiSequence> Process(IObservable<Tuple<MidiSequence, TimeSignature>> source)
        {
            return source.Select(pair => Assign(pair.Item1, pair.Item2));
        }

        static MidiSequence Assign(MidiSequence sequence, TimeSignature? timeSignature)
        {
            timeSignature?.Validate();
            return sequence.WithTimeSignature(timeSignature);
        }
    }
}
