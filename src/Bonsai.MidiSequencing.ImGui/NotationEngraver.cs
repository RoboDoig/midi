using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Melanchall.DryWetMidi.Core;

namespace Bonsai.MidiSequencing.ImGui
{
    class NotationOptions
    {
        public StaffClef Clef { get; set; }

        public int Resolution { get; set; } = 16;

        public float StaffSpacing { get; set; } = 8;
    }

    // Converts MIDI sequences into common music notation. Durations are measured in units of the
    // quantization grid, and distances are measured in staff spaces until they are recorded.
    static class NotationEngraver
    {
        // Diatonic step of each pitch class, and whether it is spelled with a sharp
        static readonly int[] Steps = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
        static readonly bool[] Sharps = { false, true, false, true, false, false, true, false, true, false, true, false };

        const int NoAccidental = 0;
        const int SharpAccidental = 1;
        const int NaturalAccidental = 2;

        const float HeadRadiusX = 0.59f;
        const float HeadRadiusY = 0.42f;
        const float HeadRotation = -0.35f;
        const float WholeRadiusX = 0.75f;
        const float StemLength = 3.5f;
        const float StemThickness = 0.12f;
        const float BeamThickness = 0.5f;
        const float BeamSpacing = 0.75f;
        const float FlagSpacing = 0.8f;
        const float LineThickness = 0.1f;
        const float BarLineThickness = 0.15f;
        const float HeaderClefWidth = 3.6f;
        const float HeaderTimeWidth = 2.6f;
        const float Margin = 0.5f;
        const float BraceWidth = 1.2f;
        const float MaxStretch = 3;

        sealed class Staff
        {
            public Staff(bool treble)
            {
                Treble = treble;
            }

            public bool Treble { get; }

            public int BottomLine => Treble ? 30 : 18;

            public int MiddleLine => BottomLine + 4;

            public int TopLine => BottomLine + 8;

            public List<(int Start, int End, int Pitch)> Notes { get; } = new();

            public List<Element> Elements { get; } = new();

            // Space needed beyond the staff lines, and the offset of the top line from the top of each system
            public float Above { get; set; }

            public float Below { get; set; }

            public float Offset { get; set; }

            public float Y(float systemTop, int position) => systemTop + Offset + (TopLine - position) * 0.5f;
        }

        sealed class Element
        {
            public int Bar;
            public int Position;
            public int Length;
            public int Base;
            public bool Dotted;
            public int[]? Pitches;
            public int[]? Positions;
            public int[]? Accidentals;
            public bool TieToNext;
            public bool WholeBarRest;
            public int Flags;
            public int Group = -1;
            public bool StemUp;
            public float[]? HeadOffsets;
            public Element? Next;

            public int System;
            public float SystemTop;
            public float X;
            public float StemTip;

            public bool IsRest => Pitches == null;

            public bool Beamed => Group >= 0;

            public float HeadX(int index) => X + (HeadOffsets != null ? HeadOffsets[index] : 0);

            // Stems attach just inside the edge of the tilted notehead
            public float StemX => StemUp ? X + HeadRadiusX - 0.06f - StemThickness / 2 : X - HeadRadiusX + 0.06f + StemThickness / 2;
        }

        sealed class Bar
        {
            public Dictionary<int, float> Columns { get; } = new();

            public float Width;
            public int System;
            public float Start;
            public float Scale = 1;

            public float End => Start + Width * Scale;

            public float X(int position) => Start + Columns[position] * Scale;
        }

        sealed class SystemLayout
        {
            public int FirstBar;
            public int LastBar;
            public float Left;
            public float ContentLeft;
            public float Right;
            public float Top;
            public float Height;
        }

        public static NotationDrawing Engrave(MidiSequence sequence, NotationOptions options, float width)
        {
            var spacing = Math.Max(2f, options.StaffSpacing);
            var timeSignature = sequence.TimeSignature ?? new TimeSignature(4, 4);
            try { timeSignature.Validate(); }
            catch (InvalidOperationException) { timeSignature = new TimeSignature(4, 4); }

            var resolution = 4;
            while (resolution < Math.Min(options.Resolution, 64)) resolution *= 2;
            resolution = Math.Max(resolution, timeSignature.BeatUnit);

            var barUnits = timeSignature.BeatsPerBar * resolution / timeSignature.BeatUnit;
            var quarterUnits = resolution / 4;
            var beatUnits = resolution / timeSignature.BeatUnit;
            var compound = timeSignature.BeatsPerBar % 3 == 0 && timeSignature.BeatsPerBar > 3 && timeSignature.BeatUnit >= 8;
            if (compound)
            {
                // Compound meters are counted in dotted beats
                beatUnits *= 3;
            }

            var beamUnits = beatUnits * 2 <= quarterUnits * 3 ? beatUnits : quarterUnits;
            var values = NoteValues(resolution);

            var notes = ExtractNotes(sequence, 4.0 / resolution);
            var totalUnits = (int)Math.Round(sequence.Length * resolution / 4.0);
            if (notes.Count > 0) totalUnits = Math.Max(totalUnits, notes.Max(note => note.End));
            var barCount = Math.Max(1, (totalUnits + barUnits - 1) / barUnits);
            totalUnits = barCount * barUnits;

            var staves = CreateStaves(notes, options.Clef);
            var group = 0;
            foreach (var staff in staves)
            {
                BuildElements(staff, totalUnits, barUnits, beatUnits, compound, values);
                AssignAccidentals(staff);
                AssignFlags(staff, quarterUnits);
                AssignStems(staff, beamUnits, ref group);
            }

            var bars = LayoutBars(staves, barCount, barUnits, quarterUnits);
            var systems = LayoutSystems(bars, staves, width / spacing);
            foreach (var element in staves.SelectMany(staff => staff.Elements))
            {
                var bar = bars[element.Bar];
                element.System = bar.System;
                element.SystemTop = systems[bar.System].Top;
                element.X = element.WholeBarRest ? (bar.Start + bar.End) / 2 : bar.X(element.Position);
            }

            var drawing = new NotationDrawing();
            var canvas = new Canvas(drawing, spacing);
            for (int i = 0; i < systems.Count; i++)
            {
                DrawSystem(canvas, systems[i], i == 0, i == systems.Count - 1, staves, bars, timeSignature);
            }

            foreach (var staff in staves)
            {
                foreach (var element in staff.Elements)
                {
                    if (element.IsRest) DrawRest(canvas, staff, element, resolution);
                    else DrawNote(canvas, staff, element, resolution);
                }

                DrawBeams(canvas, staff);
                DrawTies(canvas, staff, systems, resolution);
            }

            var last = systems[systems.Count - 1];
            drawing.Width = (systems.Max(system => system.Right) + Margin) * spacing;
            drawing.Height = (last.Top + last.Height) * spacing;
            return drawing;
        }

        static List<(int Length, int Base, bool Dotted)> NoteValues(int resolution)
        {
            var values = new List<(int Length, int Base, bool Dotted)>();
            for (int value = resolution; value >= 1; value /= 2)
            {
                if (value >= 2) values.Add((value * 3 / 2, value, true));
                values.Add((value, value, false));
            }

            return values.OrderByDescending(value => value.Length).ToList();
        }

        static List<(int Start, int End, int Pitch)> ExtractNotes(MidiSequence sequence, double unit)
        {
            var open = new Dictionary<(int Channel, int Pitch), double>();
            var notes = new List<(int Start, int End, int Pitch)>();

            void Close((int Channel, int Pitch) key, double beat)
            {
                if (!open.TryGetValue(key, out var start)) return;
                open.Remove(key);

                // Note offs are rounded up, so notes shortened by a gate keep their notated length
                var startUnit = (int)Math.Round(start / unit);
                var endUnit = (int)Math.Ceiling(beat / unit - 0.05);
                notes.Add((startUnit, Math.Max(startUnit + 1, endUnit), key.Pitch));
            }

            foreach (var sequenceEvent in sequence.Events)
            {
                switch (sequenceEvent.Event)
                {
                    case NoteOnEvent noteOn when noteOn.Velocity > 0:
                        var key = ((int)noteOn.Channel, (int)noteOn.NoteNumber);
                        Close(key, sequenceEvent.Beat);
                        open[key] = sequenceEvent.Beat;
                        break;
                    case NoteOnEvent noteOn:
                        Close((noteOn.Channel, noteOn.NoteNumber), sequenceEvent.Beat);
                        break;
                    case NoteOffEvent noteOff:
                        Close((noteOff.Channel, noteOff.NoteNumber), sequenceEvent.Beat);
                        break;
                }
            }

            foreach (var key in open.Keys.ToArray())
            {
                Close(key, sequence.Length);
            }

            return notes;
        }

        static List<Staff> CreateStaves(List<(int Start, int End, int Pitch)> notes, StaffClef clef)
        {
            if (clef == StaffClef.Auto)
            {
                if (notes.Count == 0 || notes.Min(note => note.Pitch) >= 55) clef = StaffClef.Treble;
                else if (notes.Max(note => note.Pitch) < 65) clef = StaffClef.Bass;
                else clef = StaffClef.Grand;
            }

            var staves = new List<Staff>();
            if (clef != StaffClef.Bass) staves.Add(new Staff(treble: true));
            if (clef != StaffClef.Treble) staves.Add(new Staff(treble: false));
            foreach (var note in notes)
            {
                var staff = staves.Count == 1 || note.Pitch >= 60 ? staves[0] : staves[1];
                staff.Notes.Add(note);
            }

            return staves;
        }

        static int StaffPosition(int pitch, out bool sharp)
        {
            var pitchClass = pitch % 12;
            sharp = Sharps[pitchClass];
            return (pitch / 12 - 1) * 7 + Steps[pitchClass];
        }

        // Groups notes into chords starting at each onset, with rests filling the gaps
        static void BuildElements(Staff staff, int totalUnits, int barUnits, int beatUnits, bool compound, List<(int Length, int Base, bool Dotted)> values)
        {
            var onsets = staff.Notes.GroupBy(note => note.Start).OrderBy(onset => onset.Key).ToArray();
            var time = 0;
            for (int i = 0; i < onsets.Length && onsets[i].Key < totalUnits; i++)
            {
                var start = onsets[i].Key;
                if (start > time)
                {
                    AddSegment(staff, time, start, null, barUnits, beatUnits, compound, values);
                }

                // Notes held past the next onset are cut short, since each staff has a single voice
                var end = onsets[i].Max(note => note.End);
                if (i + 1 < onsets.Length) end = Math.Min(end, onsets[i + 1].Key);
                end = Math.Min(end, totalUnits);
                var pitches = onsets[i].Select(note => note.Pitch).Distinct().OrderBy(pitch => pitch).ToArray();
                AddSegment(staff, start, end, pitches, barUnits, beatUnits, compound, values);
                time = end;
            }

            if (time < totalUnits)
            {
                AddSegment(staff, time, totalUnits, null, barUnits, beatUnits, compound, values);
            }

            for (int i = 0; i < staff.Elements.Count - 1; i++)
            {
                staff.Elements[i].Next = staff.Elements[i + 1];
            }
        }

        // Splits a note or rest at bar lines and into notatable values, tying the pieces of notes
        static void AddSegment(Staff staff, int start, int end, int[]? pitches, int barUnits, int beatUnits, bool compound, List<(int Length, int Base, bool Dotted)> values)
        {
            var time = start;
            Element? previous = null;
            while (time < end)
            {
                var bar = time / barUnits;
                var barEnd = (bar + 1) * barUnits;
                var pieceEnd = Math.Min(end, barEnd);
                var position = time % barUnits;
                if (pitches == null && position == 0 && pieceEnd == barEnd)
                {
                    staff.Elements.Add(new Element { Bar = bar, Length = barUnits, Base = barUnits, WholeBarRest = true });
                    time = pieceEnd;
                    continue;
                }

                for (var remaining = pieceEnd - time; remaining > 0;)
                {
                    var value = values.First(value =>
                        value.Length <= remaining && IsNotatable(position, value.Length, value.Base, value.Dotted, beatUnits, compound, pitches == null));
                    var element = new Element
                    {
                        Bar = bar,
                        Position = position,
                        Length = value.Length,
                        Base = value.Base,
                        Dotted = value.Dotted,
                        Pitches = pitches
                    };

                    if (previous != null && pitches != null) previous.TieToNext = true;
                    staff.Elements.Add(element);
                    previous = element;
                    position += value.Length;
                    remaining -= value.Length;
                }

                time = pieceEnd;
            }
        }

        // Values may only span several beats when they start on a beat, so the beats stay visible
        static bool IsNotatable(int position, int length, int baseLength, bool dotted, int beatUnits, bool compound, bool rest)
        {
            var nextBeat = (position / beatUnits + 1) * beatUnits;
            var end = position + length;
            if (rest && length > beatUnits && position % length != 0)
            {
                // Rests longer than a beat must start on a multiple of their own length
                return false;
            }

            if (rest && dotted && !compound && baseLength >= beatUnits)
            {
                // Rests are only dotted below the beat in simple meters
                return false;
            }

            if (position % beatUnits == 0)
            {
                return end % beatUnits == 0 || end < nextBeat || (dotted && baseLength >= beatUnits);
            }

            return end <= nextBeat && position % Math.Max(1, baseLength / 2) == 0;
        }

        static void AssignAccidentals(Staff staff)
        {
            // Accidentals apply to the rest of the bar, and the key is always C major
            var sharps = new Dictionary<int, bool>();
            var bar = -1;
            Element? previous = null;
            foreach (var element in staff.Elements)
            {
                if (element.Bar != bar)
                {
                    sharps.Clear();
                    bar = element.Bar;
                }

                if (element.Pitches != null)
                {
                    var tied = previous != null && previous.TieToNext;
                    element.Positions = new int[element.Pitches.Length];
                    element.Accidentals = new int[element.Pitches.Length];
                    for (int i = 0; i < element.Pitches.Length; i++)
                    {
                        var position = StaffPosition(element.Pitches[i], out var sharp);
                        element.Positions[i] = position;

                        // Tied notes continue the accidental of the note they are tied from
                        if (tied) continue;
                        sharps.TryGetValue(position, out var current);
                        if (sharp != current)
                        {
                            element.Accidentals[i] = sharp ? SharpAccidental : NaturalAccidental;
                            sharps[position] = sharp;
                        }
                    }
                }

                previous = element;
            }
        }

        static void AssignFlags(Staff staff, int quarterUnits)
        {
            foreach (var element in staff.Elements)
            {
                for (int value = element.Base; value < quarterUnits; value *= 2)
                {
                    element.Flags++;
                }
            }
        }

        static void AssignStems(Staff staff, int beamUnits, ref int group)
        {
            var elements = staff.Elements;
            var i = 0;
            while (i < elements.Count)
            {
                var element = elements[i];
                if (element.IsRest)
                {
                    i++;
                    continue;
                }

                // Consecutive notes shorter than a quarter note are beamed within each beat
                var j = i + 1;
                if (element.Flags > 0)
                {
                    while (j < elements.Count &&
                           !elements[j].IsRest &&
                           elements[j].Flags > 0 &&
                           elements[j].Bar == element.Bar &&
                           elements[j].Position / beamUnits == element.Position / beamUnits)
                    {
                        j++;
                    }
                }

                // Stems point away from the note furthest from the middle line
                var notes = elements.GetRange(i, j - i);
                var above = notes.Max(note => note.Positions!.Max() - staff.MiddleLine);
                var below = notes.Max(note => staff.MiddleLine - note.Positions!.Min());
                foreach (var note in notes)
                {
                    note.StemUp = below > above;
                    note.Group = notes.Count > 1 ? group : -1;
                    note.HeadOffsets = HeadOffsets(note.Positions!, note.StemUp);
                }

                if (notes.Count > 1) group++;
                i = j;
            }
        }

        // Noteheads a step apart in a chord are placed on opposite sides of the stem
        static float[] HeadOffsets(int[] positions, bool stemUp)
        {
            var offsets = new float[positions.Length];
            var shift = 2 * HeadRadiusX - StemThickness;
            if (stemUp)
            {
                for (int i = 1; i < positions.Length; i++)
                {
                    if (positions[i] - positions[i - 1] == 1 && offsets[i - 1] == 0) offsets[i] = shift;
                }
            }
            else
            {
                for (int i = positions.Length - 2; i >= 0; i--)
                {
                    if (positions[i + 1] - positions[i] == 1 && offsets[i + 1] == 0) offsets[i] = -shift;
                }
            }

            return offsets;
        }

        // Notes starting at the same time on different staves share a column, and the space
        // after each column grows with its duration
        static List<Bar> LayoutBars(List<Staff> staves, int barCount, int barUnits, int quarterUnits)
        {
            var bars = new List<Bar>();
            for (int b = 0; b < barCount; b++)
            {
                var elements = staves.SelectMany(staff => staff.Elements.Where(element => element.Bar == b)).ToArray();
                var positions = elements.Select(element => element.Position).Distinct().OrderBy(position => position).ToArray();
                var bar = new Bar();
                var x = 1.2f;
                for (int i = 0; i < positions.Length; i++)
                {
                    var column = elements.Where(element => element.Position == positions[i]).ToArray();
                    var accidentals = column.Max(element => element.Accidentals?.Count(accidental => accidental != NoAccidental) ?? 0);
                    if (accidentals > 0) x += 0.6f + Math.Min(accidentals, 3) * 1.0f;
                    if (column.Any(element => element.HeadOffsets?.Any(offset => offset < 0) == true)) x += 2 * HeadRadiusX;
                    bar.Columns[positions[i]] = x;

                    var gap = (i + 1 < positions.Length ? positions[i + 1] : barUnits) - positions[i];
                    x += 1.3f + 1.7f * (float)Math.Sqrt((double)gap / quarterUnits);
                    if (column.Any(element => element.Dotted)) x += 0.6f;
                    if (column.Any(element => element.HeadOffsets?.Any(offset => offset > 0) == true)) x += 2 * HeadRadiusX;
                }

                bar.Width = x;
                bars.Add(bar);
            }

            return bars;
        }

        // Breaks bars into systems which fit the available width, and stretches each system to fill it
        static List<SystemLayout> LayoutSystems(List<Bar> bars, List<Staff> staves, float width)
        {
            var left = Margin + (staves.Count > 1 ? BraceWidth : 0);
            var systems = new List<SystemLayout>();
            var barIndex = 0;
            while (barIndex < bars.Count)
            {
                var system = new SystemLayout { FirstBar = barIndex, Left = left };
                system.ContentLeft = left + HeaderClefWidth + (systems.Count == 0 ? HeaderTimeWidth : 0);
                var available = width - system.ContentLeft - Margin;
                var natural = 0f;
                while (barIndex < bars.Count && (barIndex == system.FirstBar || natural + bars[barIndex].Width <= available))
                {
                    natural += bars[barIndex++].Width;
                }

                system.LastBar = barIndex - 1;
                var scale = 1f;
                if (available > 0)
                {
                    // The last system is only stretched when it is the only one
                    if (barIndex < bars.Count || systems.Count == 0) scale = Math.Min(MaxStretch, available / natural);
                    else if (natural > available) scale = available / natural;
                }

                var x = system.ContentLeft;
                for (int b = system.FirstBar; b <= system.LastBar; b++)
                {
                    bars[b].System = systems.Count;
                    bars[b].Start = x;
                    bars[b].Scale = scale;
                    x = bars[b].End;
                }

                system.Right = x;
                systems.Add(system);
            }

            // Leave room for ledger lines and stems beyond the staff lines
            foreach (var staff in staves)
            {
                var positions = staff.Notes.Count > 0
                    ? staff.Notes.Select(note => StaffPosition(note.Pitch, out _)).ToArray()
                    : new[] { staff.MiddleLine };
                staff.Above = Math.Max(3, (positions.Max() - staff.TopLine) * 0.5f + StemLength);
                staff.Below = Math.Max(3, (staff.BottomLine - positions.Min()) * 0.5f + StemLength);
            }

            var offset = staves[0].Above;
            for (int i = 0; i < staves.Count; i++)
            {
                if (i > 0) offset += Math.Max(6, staves[i - 1].Below + staves[i].Above - 3);
                staves[i].Offset = offset;
                offset += 4;
            }

            var height = offset + staves[staves.Count - 1].Below;
            for (int i = 0; i < systems.Count; i++)
            {
                systems[i].Top = i * height;
                systems[i].Height = height;
            }

            return systems;
        }

        static void DrawSystem(Canvas canvas, SystemLayout system, bool isFirst, bool isLast, List<Staff> staves, List<Bar> bars, TimeSignature timeSignature)
        {
            var top = staves[0].Y(system.Top, staves[0].TopLine);
            var bottom = staves[staves.Count - 1].Y(system.Top, staves[staves.Count - 1].BottomLine);
            foreach (var staff in staves)
            {
                var staffTop = staff.Y(system.Top, staff.TopLine);
                for (int line = 0; line < 5; line++)
                {
                    canvas.Line(new Vector2(system.Left, staffTop + line), new Vector2(system.Right, staffTop + line), LineThickness);
                }

                if (staff.Treble) DrawTrebleClef(canvas, system.Left + 1.5f, staffTop + 3);
                else DrawBassClef(canvas, system.Left + 0.6f, staffTop + 1);

                if (isFirst)
                {
                    var x = system.Left + HeaderClefWidth + HeaderTimeWidth / 2 - 0.3f;
                    canvas.Text(new Vector2(x, staffTop + 1), 2.6f, timeSignature.BeatsPerBar.ToString());
                    canvas.Text(new Vector2(x, staffTop + 3), 2.6f, timeSignature.BeatUnit.ToString());
                }
            }

            if (staves.Count > 1)
            {
                // A bracket and a system line join the staves of a grand staff
                var x = system.Left - 0.8f;
                canvas.Rectangle(new Vector2(x - 0.25f, top - 0.6f), new Vector2(x + 0.25f, bottom + 0.6f));
                canvas.Bezier(new Vector2(x - 0.25f, top - 0.5f), new Vector2(x + 0.3f, top - 0.6f), new Vector2(x + 0.8f, top - 0.9f), new Vector2(x + 1.0f, top - 1.4f), 0.2f);
                canvas.Bezier(new Vector2(x - 0.25f, bottom + 0.5f), new Vector2(x + 0.3f, bottom + 0.6f), new Vector2(x + 0.8f, bottom + 0.9f), new Vector2(x + 1.0f, bottom + 1.4f), 0.2f);
                canvas.Line(new Vector2(system.Left, top), new Vector2(system.Left, bottom), BarLineThickness);
            }

            for (int b = system.FirstBar; b <= system.LastBar; b++)
            {
                var x = bars[b].End;
                if (isLast && b == system.LastBar)
                {
                    canvas.Line(new Vector2(x - 0.9f, top), new Vector2(x - 0.9f, bottom), BarLineThickness);
                    canvas.Rectangle(new Vector2(x - 0.5f, top - LineThickness / 2), new Vector2(x, bottom + LineThickness / 2));
                }
                else
                {
                    canvas.Line(new Vector2(x, top), new Vector2(x, bottom), BarLineThickness);
                }
            }
        }

        static void DrawNote(Canvas canvas, Staff staff, Element element, int resolution)
        {
            float Y(int position) => staff.Y(element.SystemTop, position);
            var positions = element.Positions!;
            var whole = element.Base >= resolution;
            var hollow = element.Base * 2 >= resolution;
            var radiusX = whole ? WholeRadiusX : HeadRadiusX;
            for (int i = 0; i < positions.Length; i++)
            {
                var headX = element.HeadX(i);
                var y = Y(positions[i]);
                for (int line = staff.BottomLine - 2; line >= positions[i]; line -= 2)
                {
                    canvas.Line(new Vector2(headX - radiusX - 0.45f, Y(line)), new Vector2(headX + radiusX + 0.45f, Y(line)), 0.15f);
                }

                for (int line = staff.TopLine + 2; line <= positions[i]; line += 2)
                {
                    canvas.Line(new Vector2(headX - radiusX - 0.45f, Y(line)), new Vector2(headX + radiusX + 0.45f, Y(line)), 0.15f);
                }

                if (whole) canvas.Ellipse(new Vector2(headX, y), new Vector2(WholeRadiusX, 0.47f), 0, filled: false, thickness: 0.24f);
                else canvas.Ellipse(new Vector2(headX, y), new Vector2(HeadRadiusX, HeadRadiusY), HeadRotation, filled: !hollow, thickness: 0.17f);

                if (element.Dotted)
                {
                    // Dots on notes on a line move up into the space above
                    var onLine = ((positions[i] - staff.BottomLine) % 2 + 2) % 2 == 0;
                    var dotX = element.X + Math.Max(0, element.HeadOffsets!.Max()) + radiusX + 0.55f;
                    canvas.Circle(new Vector2(dotX, y - (onLine ? 0.5f : 0)), 0.2f);
                }
            }

            // Accidentals move further left when they would collide with the one above
            var column = 0;
            var previous = int.MaxValue;
            for (int i = positions.Length - 1; i >= 0; i--)
            {
                var accidental = element.Accidentals![i];
                if (accidental == NoAccidental) continue;
                column = previous - positions[i] < 6 ? column + 1 : 0;
                previous = positions[i];
                var x = element.X + Math.Min(0, element.HeadOffsets!.Min()) - radiusX - 0.85f - column * 1.0f;
                if (accidental == SharpAccidental) DrawSharp(canvas, x, Y(positions[i]));
                else DrawNatural(canvas, x, Y(positions[i]));
            }

            if (whole) return;

            // Stems reach at least the middle line, and grow to make room for extra flags
            var middle = Y(staff.MiddleLine);
            var extra = element.Beamed ? 0 : Math.Max(0, element.Flags - 1) * FlagSpacing;
            element.StemTip = element.StemUp
                ? Math.Min(Y(positions[positions.Length - 1]) - StemLength - extra, middle)
                : Math.Max(Y(positions[0]) + StemLength + extra, middle);
            if (element.Beamed) return;

            canvas.Line(new Vector2(element.StemX, StemBase(staff, element)), new Vector2(element.StemX, element.StemTip), StemThickness);
            var direction = element.StemUp ? 1 : -1;
            for (int flag = 0; flag < element.Flags; flag++)
            {
                var x = element.StemX;
                var y = element.StemTip + direction * flag * FlagSpacing;
                canvas.Bezier(
                    new Vector2(x, y),
                    new Vector2(x + 0.1f, y + direction * 1.1f),
                    new Vector2(x + 1.3f, y + direction * 1.3f),
                    new Vector2(x + 0.9f, y + direction * 2.9f),
                    0.28f);
            }
        }

        static float StemBase(Staff staff, Element element)
        {
            var positions = element.Positions!;
            return element.StemUp
                ? staff.Y(element.SystemTop, positions[0]) + 0.1f
                : staff.Y(element.SystemTop, positions[positions.Length - 1]) - 0.1f;
        }

        static void DrawBeams(Canvas canvas, Staff staff)
        {
            foreach (var group in staff.Elements.Where(element => element.Beamed).GroupBy(element => element.Group))
            {
                // Beams are horizontal and placed at the furthest stem tip in the group
                var notes = group.ToArray();
                var stemUp = notes[0].StemUp;
                var levels = notes.Max(note => note.Flags);
                var direction = stemUp ? 1 : -1;
                var tip = stemUp
                    ? notes.Min(note => note.StemTip) - (levels - 1) * BeamSpacing
                    : notes.Max(note => note.StemTip) + (levels - 1) * BeamSpacing;
                foreach (var note in notes)
                {
                    canvas.Line(new Vector2(note.StemX, StemBase(staff, note)), new Vector2(note.StemX, tip), StemThickness);
                }

                for (int level = 0; level < levels; level++)
                {
                    var y0 = tip + direction * level * BeamSpacing;
                    var y1 = y0 + direction * BeamThickness;
                    for (int k = 0; k < notes.Length; k++)
                    {
                        if (notes[k].Flags <= level) continue;
                        var hasNext = k + 1 < notes.Length && notes[k + 1].Flags > level;
                        var hasPrevious = k > 0 && notes[k - 1].Flags > level;
                        if (hasNext) Beam(canvas, notes[k].StemX, notes[k + 1].StemX, y0, y1);
                        else if (!hasPrevious)
                        {
                            // Partial beams point towards the previous note in the group, if any
                            if (k > 0) Beam(canvas, notes[k].StemX - 1.1f, notes[k].StemX, y0, y1);
                            else Beam(canvas, notes[k].StemX, notes[k].StemX + 1.1f, y0, y1);
                        }
                    }
                }
            }
        }

        static void Beam(Canvas canvas, float x0, float x1, float y0, float y1)
        {
            canvas.Rectangle(
                new Vector2(x0 - StemThickness / 2, Math.Min(y0, y1)),
                new Vector2(x1 + StemThickness / 2, Math.Max(y0, y1)));
        }

        static void DrawTies(Canvas canvas, Staff staff, List<SystemLayout> systems, int resolution)
        {
            foreach (var element in staff.Elements)
            {
                if (!element.TieToNext || element.Next is not Element next || next.Positions == null) continue;
                var positions = element.Positions!;
                var radiusX = element.Base >= resolution ? WholeRadiusX : HeadRadiusX;
                var nextRadiusX = next.Base >= resolution ? WholeRadiusX : HeadRadiusX;
                for (int i = 0; i < positions.Length; i++)
                {
                    // Ties curve away from the stem, or away from the middle of a chord
                    var up = positions.Length > 1 ? i >= positions.Length / 2 : !element.StemUp;
                    var direction = up ? -1 : 1;
                    var x0 = element.HeadX(i) + radiusX + (element.Dotted ? 0.8f : 0.1f);
                    var y0 = staff.Y(element.SystemTop, positions[i]) + direction * 0.5f;
                    var y1 = staff.Y(next.SystemTop, positions[i]) + direction * 0.5f;
                    var x1 = next.HeadX(i) - nextRadiusX - 0.1f;
                    if (next.System == element.System)
                    {
                        Tie(canvas, x0, x1, y0, direction);
                    }
                    else
                    {
                        Tie(canvas, x0, systems[element.System].Right - 0.2f, y0, direction);
                        Tie(canvas, systems[next.System].ContentLeft - 0.8f, x1, y1, direction);
                    }
                }
            }
        }

        static void Tie(Canvas canvas, float x0, float x1, float y, int direction)
        {
            var height = direction * Math.Min(1.0f, 0.3f + (x1 - x0) * 0.06f);
            var width = x1 - x0;
            canvas.Bezier(
                new Vector2(x0, y),
                new Vector2(x0 + width * 0.2f, y + height),
                new Vector2(x0 + width * 0.8f, y + height),
                new Vector2(x1, y),
                0.17f);
        }

        static void DrawRest(Canvas canvas, Staff staff, Element element, int resolution)
        {
            float Y(float position) => staff.Y(element.SystemTop, 0) - position * 0.5f;
            var x = element.X;
            var middle = Y(staff.MiddleLine);
            if (element.WholeBarRest || element.Base >= resolution)
            {
                // Whole rests hang from the fourth line
                var y = Y(staff.MiddleLine + 2);
                canvas.Rectangle(new Vector2(x - 0.6f, y), new Vector2(x + 0.6f, y + 0.5f));
            }
            else if (element.Base * 2 >= resolution)
            {
                // Half rests sit on the middle line
                canvas.Rectangle(new Vector2(x - 0.6f, middle - 0.5f), new Vector2(x + 0.6f, middle));
            }
            else if (element.Flags == 0)
            {
                canvas.Polyline(new[]
                {
                    new Vector2(x - 0.3f, middle - 1.5f),
                    new Vector2(x + 0.35f, middle - 0.75f),
                    new Vector2(x - 0.2f, middle - 0.1f),
                    new Vector2(x + 0.4f, middle + 0.65f)
                }, 0.3f);
                canvas.Bezier(
                    new Vector2(x + 0.4f, middle + 0.65f),
                    new Vector2(x - 0.2f, middle + 0.3f),
                    new Vector2(x - 0.6f, middle + 0.9f),
                    new Vector2(x + 0.05f, middle + 1.4f),
                    0.25f);
            }
            else
            {
                // Each flag of a shorter rest adds a hook further down the slanted stem
                var flags = element.Flags;
                var top = Y(staff.MiddleLine + 2) + 0.5f;
                var stemTop = new Vector2(x + 0.5f, top - 0.1f);
                var stemBottom = new Vector2(x - 0.1f - (flags - 1) * 0.3f, top + 1.3f + (flags - 1) * 1.0f);
                canvas.Line(stemTop, stemBottom, 0.15f);
                for (int flag = 0; flag < flags; flag++)
                {
                    var y = top + flag * 1.0f;
                    var t = (y - stemTop.Y) / (stemBottom.Y - stemTop.Y);
                    var stemX = stemTop.X + (stemBottom.X - stemTop.X) * t;
                    var dot = new Vector2(stemX - 0.75f, y + 0.1f);
                    canvas.Circle(dot, 0.25f);
                    canvas.Bezier(
                        new Vector2(dot.X, dot.Y + 0.15f),
                        new Vector2(dot.X + 0.3f, dot.Y + 0.4f),
                        new Vector2(stemX - 0.25f, y + 0.3f),
                        new Vector2(stemX, y),
                        0.15f);
                }
            }

            if (element.Dotted)
            {
                canvas.Circle(new Vector2(x + 1.0f, Y(staff.MiddleLine + 1)), 0.2f);
            }
        }

        static void DrawSharp(Canvas canvas, float x, float y)
        {
            canvas.Line(new Vector2(x - 0.22f, y - 1.3f), new Vector2(x - 0.22f, y + 1.45f), 0.12f);
            canvas.Line(new Vector2(x + 0.22f, y - 1.45f), new Vector2(x + 0.22f, y + 1.3f), 0.12f);
            canvas.Polygon(new Vector2(x - 0.55f, y - 0.3f), new Vector2(x + 0.55f, y - 0.65f), new Vector2(x + 0.55f, y - 0.3f), new Vector2(x - 0.55f, y + 0.05f));
            canvas.Polygon(new Vector2(x - 0.55f, y + 0.55f), new Vector2(x + 0.55f, y + 0.2f), new Vector2(x + 0.55f, y + 0.55f), new Vector2(x - 0.55f, y + 0.9f));
        }

        static void DrawNatural(Canvas canvas, float x, float y)
        {
            canvas.Line(new Vector2(x - 0.3f, y - 1.4f), new Vector2(x - 0.3f, y + 0.55f), 0.12f);
            canvas.Line(new Vector2(x + 0.3f, y - 0.55f), new Vector2(x + 0.3f, y + 1.4f), 0.12f);
            canvas.Polygon(new Vector2(x - 0.3f, y - 0.35f), new Vector2(x + 0.3f, y - 0.55f), new Vector2(x + 0.3f, y - 0.2f), new Vector2(x - 0.3f, y));
            canvas.Polygon(new Vector2(x - 0.3f, y + 0.2f), new Vector2(x + 0.3f, y), new Vector2(x + 0.3f, y + 0.35f), new Vector2(x - 0.3f, y + 0.55f));
        }

        // The treble clef curls around the G line, at the specified position
        static void DrawTrebleClef(Canvas canvas, float x, float y)
        {
            var knots = new[]
            {
                new Vector2(0.1f, 0.0f), new Vector2(0.45f, -0.35f), new Vector2(0.15f, -0.75f), new Vector2(-0.45f, -0.55f),
                new Vector2(-0.7f, 0.05f), new Vector2(-0.35f, 0.75f), new Vector2(0.4f, 0.9f), new Vector2(0.95f, 0.35f),
                new Vector2(1.0f, -0.4f), new Vector2(0.55f, -1.1f), new Vector2(-0.1f, -1.6f), new Vector2(-0.55f, -2.4f),
                new Vector2(-0.45f, -3.4f), new Vector2(0.05f, -4.3f), new Vector2(0.4f, -4.4f), new Vector2(0.45f, -3.9f),
                new Vector2(0.1f, -3.2f), new Vector2(-0.15f, -2.2f), new Vector2(0.05f, -0.5f), new Vector2(0.25f, 1.5f),
                new Vector2(0.2f, 2.2f), new Vector2(-0.15f, 2.5f), new Vector2(-0.55f, 2.3f), new Vector2(-0.6f, 1.9f)
            };
            canvas.Polyline(Spline(knots, new Vector2(x, y)), 0.2f);
            canvas.Circle(new Vector2(x - 0.35f, y + 2.0f), 0.32f);
        }

        // The bass clef starts with a dot on the F line, at the specified position
        static void DrawBassClef(Canvas canvas, float x, float y)
        {
            var knots = new[]
            {
                new Vector2(0.05f, -0.05f), new Vector2(0.2f, -0.75f), new Vector2(1.0f, -0.95f), new Vector2(1.65f, -0.35f),
                new Vector2(1.6f, 0.6f), new Vector2(0.9f, 1.65f), new Vector2(-0.3f, 2.6f)
            };
            canvas.Circle(new Vector2(x + 0.15f, y), 0.38f);
            canvas.Polyline(Spline(knots, new Vector2(x, y)), 0.26f);
            canvas.Circle(new Vector2(x + 2.25f, y - 0.5f), 0.17f);
            canvas.Circle(new Vector2(x + 2.25f, y + 0.5f), 0.17f);
        }

        // Interpolates a Catmull-Rom spline through the knots
        static Vector2[] Spline(Vector2[] knots, Vector2 origin, int steps = 8)
        {
            var points = new List<Vector2>();
            for (int i = 0; i < knots.Length - 1; i++)
            {
                var p0 = knots[Math.Max(0, i - 1)];
                var p1 = knots[i];
                var p2 = knots[i + 1];
                var p3 = knots[Math.Min(knots.Length - 1, i + 2)];
                for (int step = 0; step < steps; step++)
                {
                    var t = (float)step / steps;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    points.Add(origin + 0.5f * (
                        2 * p1 +
                        (p2 - p0) * t +
                        (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                        (3 * p1 - p0 - 3 * p2 + p3) * t3));
                }
            }

            points.Add(origin + knots[knots.Length - 1]);
            return points.ToArray();
        }

        // Scales staff space units into pixels while recording primitives
        sealed class Canvas
        {
            readonly NotationDrawing drawing;
            readonly float scale;

            public Canvas(NotationDrawing drawing, float scale)
            {
                this.drawing = drawing;
                this.scale = scale;
            }

            // Lines thinner than a pixel fade out when anti-aliased
            float Thickness(float thickness) => Math.Max(1, thickness * scale);

            public void Line(Vector2 from, Vector2 to, float thickness) => drawing.Line(from * scale, to * scale, Thickness(thickness));

            public void Polyline(Vector2[] points, float thickness) => drawing.Polyline(points.Select(point => point * scale).ToArray(), Thickness(thickness));

            public void Polygon(params Vector2[] points) => drawing.Polygon(points.Select(point => point * scale).ToArray());

            public void Rectangle(Vector2 min, Vector2 max) => drawing.Rectangle(min * scale, max * scale);

            public void Ellipse(Vector2 center, Vector2 radius, float rotation, bool filled, float thickness) =>
                drawing.Ellipse(center * scale, radius * scale, rotation, filled, Thickness(thickness));

            public void Circle(Vector2 center, float radius) => drawing.Circle(center * scale, radius * scale);

            public void Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float thickness) =>
                drawing.Bezier(p0 * scale, p1 * scale, p2 * scale, p3 * scale, Thickness(thickness));

            public void Text(Vector2 center, float size, string text) => drawing.Text(center * scale, size * scale, text);
        }
    }
}
