using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing
{
    /// <summary>
    /// Represents an operator that creates a MIDI sequence from a step pattern of notes.
    /// </summary>
    /// <remarks>
    /// The pattern is a list of steps separated by whitespace, where each step lasts
    /// <see cref="StepLength"/> beats. Each step can be one of the following:
    /// <list type="bullet">
    /// <item><description>A note name with an octave, e.g. <c>C4</c>, <c>F#3</c> or <c>Bb2</c>, where <c>C4</c> is middle C.</description></item>
    /// <item><description>A MIDI note number, e.g. <c>60</c>.</description></item>
    /// <item><description>A chord of notes separated by commas, e.g. <c>C4,E4,G4</c>.</description></item>
    /// <item><description>A rest, written as <c>.</c></description></item>
    /// <item><description>A tie, written as <c>_</c>, which holds the notes of the previous step.</description></item>
    /// </list>
    /// </remarks>
    [Description("Creates a MIDI sequence from a step pattern of notes.")]
    public class CreateNoteSequence : Source<MidiSequence>
    {
        static readonly Regex NoteNamePattern = new(@"^([A-Ga-g])([#b]*)(-?\d+)$", RegexOptions.CultureInvariant);
        static readonly int[] PitchClasses = { 9, 11, 0, 2, 4, 5, 7 };

        /// <summary>
        /// Gets or sets the step pattern of notes, rests and ties in the sequence.
        /// </summary>
        [Description("The step pattern of notes, rests (.) and ties (_) in the sequence, e.g. \"C4 E4 G4,B4 _ . C5\".")]
        public string Notes { get; set; } = "C4 E4 G4 C5";

        /// <summary>
        /// Gets or sets the length of each step, in beats.
        /// </summary>
        [Description("The length of each step, in beats.")]
        public double StepLength { get; set; } = 0.25;

        /// <summary>
        /// Gets or sets the fraction of the last step for which each note sounds.
        /// </summary>
        [Range(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The fraction of the last step for which each note sounds.")]
        public double Gate { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the velocity of all notes in the sequence, from 1 to 127.
        /// </summary>
        [Range(1, 127)]
        [Description("The velocity of all notes in the sequence, from 1 to 127.")]
        public int Velocity { get; set; } = 100;

        /// <summary>
        /// Gets or sets the MIDI channel of all notes in the sequence, from 0 to 15.
        /// </summary>
        [Range(0, 15)]
        [Description("The MIDI channel of all notes in the sequence, from 0 to 15.")]
        public int Channel { get; set; }

        /// <summary>
        /// Generates an observable sequence that contains a single MIDI sequence
        /// created from the step pattern.
        /// </summary>
        /// <returns>A sequence containing a single <see cref="MidiSequence"/> object.</returns>
        public override IObservable<MidiSequence> Generate()
        {
            return Observable.Defer(() => Observable.Return(CreateSequence()));
        }

        /// <summary>
        /// Generates an observable sequence of MIDI sequences created from the step pattern,
        /// where a new sequence is emitted whenever the source sequence emits a notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">The sequence containing the notifications used to emit new sequences.</param>
        /// <returns>A sequence of <see cref="MidiSequence"/> objects.</returns>
        public IObservable<MidiSequence> Generate<TSource>(IObservable<TSource> source)
        {
            return source.Select(_ => CreateSequence());
        }

        MidiSequence CreateSequence()
        {
            var stepLength = StepLength;
            var gate = Gate;
            if (stepLength <= 0)
            {
                throw new InvalidOperationException("The step length must be a positive number of beats.");
            }

            if (gate <= 0 || gate > 1)
            {
                throw new InvalidOperationException("The gate must be greater than zero and at most one.");
            }

            var velocity = MidiValue.ToSevenBitNumber(Velocity, nameof(Velocity));
            var channel = MidiValue.ToFourBitNumber(Channel, nameof(Channel));
            var steps = (Notes ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var groups = new List<(SevenBitNumber[] Notes, int Start, int Length)>();
            for (int i = 0; i < steps.Length; i++)
            {
                switch (steps[i])
                {
                    case "_":
                        if (groups.Count > 0 && groups[groups.Count - 1].Start + groups[groups.Count - 1].Length == i)
                        {
                            var tied = groups[groups.Count - 1];
                            groups[groups.Count - 1] = (tied.Notes, tied.Start, tied.Length + 1);
                        }
                        break;
                    case ".":
                        break;
                    default:
                        var notes = steps[i].Split(',').Select(ParseNote).ToArray();
                        groups.Add((notes, i, 1));
                        break;
                }
            }

            var events = new List<SequenceEvent>();
            foreach (var (notes, start, length) in groups)
            {
                var onBeat = start * stepLength;
                var offBeat = (start + length - 1 + gate) * stepLength;
                foreach (var note in notes)
                {
                    events.Add(new SequenceEvent(onBeat, new NoteOnEvent(note, velocity) { Channel = channel }));
                    events.Add(new SequenceEvent(offBeat, new NoteOffEvent(note, SevenBitNumber.MinValue) { Channel = channel }));
                }
            }

            return new MidiSequence(events, steps.Length * stepLength);
        }

        static SevenBitNumber ParseNote(string token)
        {
            int noteNumber;
            if (int.TryParse(token, out var number))
            {
                noteNumber = number;
            }
            else
            {
                var match = NoteNamePattern.Match(token);
                if (!match.Success)
                {
                    throw new FormatException($"Invalid note '{token}'. Notes must be note names such as C4, F#3 or Bb2, or MIDI note numbers.");
                }

                var pitchClass = PitchClasses[char.ToUpperInvariant(match.Groups[1].Value[0]) - 'A'];
                foreach (var accidental in match.Groups[2].Value)
                {
                    pitchClass += accidental == '#' ? 1 : -1;
                }

                var octave = int.Parse(match.Groups[3].Value);
                noteNumber = (octave + 1) * 12 + pitchClass;
            }

            if (noteNumber < SevenBitNumber.MinValue || noteNumber > SevenBitNumber.MaxValue)
            {
                throw new FormatException($"The note '{token}' is outside the MIDI note range.");
            }

            return (SevenBitNumber)(byte)noteNumber;
        }
    }
}
