using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that creates a MIDI sequence containing a random rhythm
    /// lasting a single bar.
    /// </summary>
    /// <remarks>
    /// The rhythm starts as a single note covering the whole bar. A number of changes between
    /// <see cref="MinChanges"/> and <see cref="MaxChanges"/> is then chosen at random, and on
    /// each change a random note is either deleted, with probability <see cref="DeleteProbability"/>,
    /// or split in two. The first change always splits the note covering the bar. Notes are measured in units
    /// of the current subdivision: an even number of units is split into two equal halves, an
    /// odd number of units is split into the nearest unequal parts in random order, e.g. a note
    /// lasting five beats is split into two and three beats, and a single unit is split into two
    /// notes of the next subdivision. Notes of <see cref="MinimumNoteLength"/> can only be deleted.
    /// The process ends early if all notes are deleted.
    /// </remarks>
    [Description("Creates a MIDI sequence containing a random rhythm lasting a single bar.")]
    public class CreateRandomRhythm : Source<MidiSequence>
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
        /// Gets or sets the note value of the shortest note in the rhythm, where 16 is a sixteenth note.
        /// </summary>
        [Description("The note value of the shortest note in the rhythm, where 16 is a sixteenth note.")]
        public int MinimumNoteLength { get; set; } = 16;

        /// <summary>
        /// Gets or sets the minimum number of changes to apply to the rhythm.
        /// </summary>
        [Description("The minimum number of changes to apply to the rhythm.")]
        public int MinChanges { get; set; } = 2;

        /// <summary>
        /// Gets or sets the maximum number of changes to apply to the rhythm.
        /// </summary>
        [Description("The maximum number of changes to apply to the rhythm.")]
        public int MaxChanges { get; set; } = 6;

        /// <summary>
        /// Gets or sets the probability that each change after the first deletes a note instead of splitting it.
        /// </summary>
        [Range(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The probability that each change after the first deletes a note instead of splitting it.")]
        public double DeleteProbability { get; set; } = 0.25;

        /// <summary>
        /// Gets or sets the MIDI note number of all notes in the rhythm, from 0 to 127, where middle C is 60.
        /// </summary>
        [Range(0, 127)]
        [Description("The MIDI note number of all notes in the rhythm, from 0 to 127, where middle C is 60.")]
        public int Pitch { get; set; } = 60;

        /// <summary>
        /// Gets or sets the fraction of the last minimum note length for which each note sounds.
        /// </summary>
        [Range(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The fraction of the last minimum note length for which each note sounds.")]
        public double Gate { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the velocity of all notes in the rhythm, from 1 to 127.
        /// </summary>
        [Range(1, 127)]
        [Description("The velocity of all notes in the rhythm, from 1 to 127.")]
        public int Velocity { get; set; } = 100;

        /// <summary>
        /// Gets or sets the MIDI channel of all notes in the rhythm, from 0 to 15.
        /// </summary>
        [Range(0, 15)]
        [Description("The MIDI channel of all notes in the rhythm, from 0 to 15.")]
        public int Channel { get; set; }

        /// <summary>
        /// Gets or sets an optional seed for the random number generator. If no value is
        /// specified, a different sequence of rhythms is generated on each subscription.
        /// </summary>
        [Description("An optional seed for the random number generator. If no value is specified, a different sequence of rhythms is generated on each subscription.")]
        public int? Seed { get; set; }

        /// <summary>
        /// Generates an observable sequence that contains a single MIDI sequence
        /// with a random rhythm.
        /// </summary>
        /// <returns>A sequence containing a single <see cref="MidiSequence"/> object.</returns>
        public override IObservable<MidiSequence> Generate()
        {
            return Observable.Defer(() => Observable.Return(CreateSequence(CreateRandom())));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI sequences with random rhythms,
        /// where a new rhythm is emitted whenever the source sequence emits a notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">The sequence containing the notifications used to emit new rhythms.</param>
        /// <returns>A sequence of <see cref="MidiSequence"/> objects.</returns>
        public IObservable<MidiSequence> Generate<TSource>(IObservable<TSource> source)
        {
            return Observable.Defer(() =>
            {
                var random = CreateRandom();
                return source.Select(_ => CreateSequence(random));
            });
        }

        Random CreateRandom()
        {
            var seed = Seed;
            return seed.HasValue ? new Random(seed.Value) : new Random();
        }

        MidiSequence CreateSequence(Random random)
        {
            var beatsPerBar = BeatsPerBar;
            var beatUnit = BeatUnit;
            var minimumNoteLength = MinimumNoteLength;
            var minChanges = MinChanges;
            var maxChanges = MaxChanges;
            var deleteProbability = DeleteProbability;
            var gate = Gate;
            if (beatsPerBar <= 0)
            {
                throw new InvalidOperationException("The number of beats per bar must be a positive number.");
            }

            if (!IsPowerOfTwo(beatUnit))
            {
                throw new InvalidOperationException("The beat unit must be a power of two, e.g. 2, 4 or 8.");
            }

            if (!IsPowerOfTwo(minimumNoteLength) || minimumNoteLength < beatUnit)
            {
                throw new InvalidOperationException("The minimum note length must be a power of two no longer than the beat unit.");
            }

            if (minChanges < 0 || maxChanges < minChanges)
            {
                throw new InvalidOperationException("The minimum number of changes must be non-negative and no greater than the maximum number of changes.");
            }

            if (deleteProbability < 0 || deleteProbability > 1)
            {
                throw new InvalidOperationException("The delete probability must be between zero and one.");
            }

            if (gate <= 0 || gate > 1)
            {
                throw new InvalidOperationException("The gate must be greater than zero and at most one.");
            }

            var pitch = MidiValue.ToSevenBitNumber(Pitch, nameof(Pitch));
            var velocity = MidiValue.ToSevenBitNumber(Velocity, nameof(Velocity));
            var channel = MidiValue.ToFourBitNumber(Channel, nameof(Channel));

            // All lengths are measured in units of the minimum note length
            var beatLength = minimumNoteLength / beatUnit;
            var barLength = beatsPerBar * beatLength;
            var notes = new List<(int Start, int Length)> { (0, barLength) };
            var changes = random.Next(minChanges, maxChanges + 1);
            for (int i = 0; i < changes && notes.Count > 0; i++)
            {
                var index = random.Next(notes.Count);
                var (start, length) = notes[index];
                // The first change never deletes, so the bar is never emptied straight away
                if (length == 1 || (i > 0 && random.NextDouble() < deleteProbability))
                {
                    if (i > 0) notes.RemoveAt(index);
                    continue;
                }

                // Count the note in units of its current subdivision, which is never longer than a beat
                var subdivision = Math.Min(length & -length, beatLength);
                var count = length / subdivision;
                var first = length / 2;
                if (count > 1 && count % 2 != 0)
                {
                    first = (count / 2 + random.Next(2)) * subdivision;
                }

                notes[index] = (start, first);
                notes.Insert(index + 1, (start + first, length - first));
            }

            var unitBeats = 4.0 / minimumNoteLength;
            var events = new List<SequenceEvent>();
            foreach (var (start, length) in notes)
            {
                var onBeat = start * unitBeats;
                var offBeat = (start + length - 1 + gate) * unitBeats;
                events.Add(new SequenceEvent(onBeat, new NoteOnEvent(pitch, velocity) { Channel = channel }));
                events.Add(new SequenceEvent(offBeat, new NoteOffEvent(pitch, SevenBitNumber.MinValue) { Channel = channel }));
            }

            return new MidiSequence(events, barLength * unitBeats);
        }

        static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
