using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that creates a time signature with the specified number
    /// of beats per bar and beat unit.
    /// </summary>
    [Description("Creates a time signature with the specified number of beats per bar and beat unit.")]
    public class CreateTimeSignature : Source<TimeSignature>
    {
        /// <summary>
        /// Gets or sets the number of beats in each bar, i.e. the upper number of the time signature.
        /// </summary>
        [Description("The number of beats in each bar, i.e. the upper number of the time signature.")]
        public int BeatsPerBar { get; set; } = 4;

        /// <summary>
        /// Gets or sets the note value of each beat, i.e. the lower number of the time signature,
        /// where 4 is a quarter note and 8 is an eighth note.
        /// </summary>
        [Description("The note value of each beat, i.e. the lower number of the time signature, where 4 is a quarter note and 8 is an eighth note.")]
        public int BeatUnit { get; set; } = 4;

        /// <summary>
        /// Generates an observable sequence that contains a single time signature.
        /// </summary>
        /// <returns>A sequence containing a single <see cref="TimeSignature"/> object.</returns>
        public override IObservable<TimeSignature> Generate()
        {
            return Observable.Defer(() => Observable.Return(CreateTimeSignatureValue()));
        }

        /// <summary>
        /// Generates an observable sequence of time signatures, where a new time signature
        /// is emitted whenever the source sequence emits a notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">The sequence containing the notifications used to emit new time signatures.</param>
        /// <returns>A sequence of <see cref="TimeSignature"/> objects.</returns>
        public IObservable<TimeSignature> Generate<TSource>(IObservable<TSource> source)
        {
            return source.Select(_ => CreateTimeSignatureValue());
        }

        TimeSignature CreateTimeSignatureValue()
        {
            var timeSignature = new TimeSignature(BeatsPerBar, BeatUnit);
            timeSignature.Validate();
            return timeSignature;
        }
    }
}
