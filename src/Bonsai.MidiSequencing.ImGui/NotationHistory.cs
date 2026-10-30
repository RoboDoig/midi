using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing.ImGui
{
    // Joins consecutive sequences into a single sequence holding the most recent bars
    static class NotationHistory
    {
        public static MidiSequence Append(MidiSequence? history, MidiSequence sequence, int bars)
        {
            var timeSignature = sequence.TimeSignature;
            if (history == null || !SameTimeSignature(history.TimeSignature, timeSignature))
            {
                // A change of meter starts a new history, since bars of different lengths cannot share a staff
                return Trim(sequence.WithTimeSignature(timeSignature), bars);
            }

            // Each sequence starts on a new bar, even if the previous sequence ended mid-bar
            var offset = history.Length;
            var events = history.Events.Concat(sequence.Events.Select(sequenceEvent =>
                new SequenceEvent(sequenceEvent.Beat + offset, sequenceEvent.Event, sequenceEvent.Track)));
            var length = offset + WholeBars(sequence.Length, BarLength(timeSignature));
            return Trim(new MidiSequence(events, length, timeSignature), bars);
        }

        // Keeps only the last bars of the sequence, restarting notes held across the cut
        public static MidiSequence Trim(MidiSequence sequence, int bars)
        {
            var barLength = BarLength(sequence.TimeSignature);
            var length = WholeBars(sequence.Length, barLength);
            var cutoff = length - bars * barLength;
            if (cutoff <= 0)
            {
                return length == sequence.Length ? sequence : new MidiSequence(sequence.Events, length, sequence.TimeSignature);
            }

            var held = new Dictionary<(int Track, int Channel, int Note), SequenceEvent>();
            var events = new List<SequenceEvent>();
            foreach (var sequenceEvent in sequence.Events)
            {
                if (sequenceEvent.Beat >= cutoff)
                {
                    events.Add(new SequenceEvent(sequenceEvent.Beat - cutoff, sequenceEvent.Event, sequenceEvent.Track));
                    continue;
                }

                switch (sequenceEvent.Event)
                {
                    case NoteOnEvent noteOn when noteOn.Velocity > 0:
                        held[(sequenceEvent.Track, noteOn.Channel, noteOn.NoteNumber)] = sequenceEvent;
                        break;
                    case NoteEvent noteEvent:
                        held.Remove((sequenceEvent.Track, noteEvent.Channel, noteEvent.NoteNumber));
                        break;
                }
            }

            events.AddRange(held.Values.Select(noteOn => new SequenceEvent(0, noteOn.Event, noteOn.Track)));
            return new MidiSequence(events, length - cutoff, sequence.TimeSignature);
        }

        static double BarLength(TimeSignature? timeSignature)
        {
            // Sequences without a time signature are drawn in 4/4
            return timeSignature != null && timeSignature.BeatsPerBar > 0 && timeSignature.BeatUnit > 0
                ? timeSignature.BarLength
                : 4;
        }

        static double WholeBars(double length, double barLength)
        {
            // Allow for rounding errors in event positions, so exact bars are not padded with an extra bar
            return Math.Ceiling(length / barLength - 1e-6) * barLength;
        }

        static bool SameTimeSignature(TimeSignature? a, TimeSignature? b)
        {
            return a?.BeatsPerBar == b?.BeatsPerBar && a?.BeatUnit == b?.BeatUnit;
        }
    }
}
