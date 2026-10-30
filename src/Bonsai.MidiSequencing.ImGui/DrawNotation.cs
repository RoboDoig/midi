using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using Hexa.NET.ImGui;

namespace Bonsai.MidiSequencing.ImGui
{
    using ImGui = Hexa.NET.ImGui.ImGui;

    /// <summary>
    /// Represents an operator that draws the most recent MIDI sequence as musical notation.
    /// </summary>
    /// <remarks>
    /// Notes are quantized to the grid specified by <see cref="Resolution"/> and drawn in the
    /// key of C major, with bar lines and beams placed according to the time signature of each
    /// sequence, or 4/4 if the sequence has no time signature. Notes starting at the same time
    /// are drawn as chords, and each staff shows a single voice, so notes held past the start
    /// of the next note are shortened. Bars wrap onto new lines to fit the available width.
    /// If <see cref="History"/> is specified, the most recent bars of consecutive sequences are
    /// displayed together.
    /// </remarks>
    [Combinator]
    [WorkflowElementIcon("Bonsai:ElementIcon.Visualizer")]
    [Description("Draws the most recent MIDI sequence as musical notation.")]
    public class DrawNotation
    {
        /// <summary>
        /// Gets or sets a value specifying whether the notation is displayed.
        /// </summary>
        [Category(nameof(CategoryAttribute.Behavior))]
        [Description("Specifies whether the notation is displayed.")]
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Gets or sets a value specifying the staves and clefs used to display the notes.
        /// </summary>
        [Description("Specifies the staves and clefs used to display the notes.")]
        public StaffClef Clef { get; set; }

        /// <summary>
        /// Gets or sets the note value of the quantization grid, where 16 is a sixteenth note.
        /// </summary>
        [Description("The note value of the quantization grid, where 16 is a sixteenth note.")]
        public int Resolution { get; set; } = 16;

        /// <summary>
        /// Gets or sets the distance between staff lines, in pixels.
        /// </summary>
        [Range(2, 64)]
        [Description("The distance between staff lines, in pixels.")]
        public float StaffSpacing { get; set; } = 8;

        /// <summary>
        /// Gets or sets the number of most recent bars to display. If zero, only the
        /// most recent sequence is displayed.
        /// </summary>
        /// <remarks>
        /// Each sequence is appended after the previous one, starting on a new bar. A sequence
        /// with a different time signature clears the history.
        /// </remarks>
        [Range(0, int.MaxValue)]
        [Description("The number of most recent bars to display. If zero, only the most recent sequence is displayed.")]
        public int History { get; set; }

        /// <summary>
        /// Draws the most recent MIDI sequence as musical notation on each frame notification.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
        /// <param name="source">An observable sequence of notifications used to draw the notation.</param>
        /// <param name="sequence">The sequence of <see cref="MidiSequence"/> objects to draw.</param>
        /// <returns>
        /// An observable sequence of drawing notifications. Notifications are omitted
        /// if the notation is not visible.
        /// </returns>
        public IObservable<TSource> Process<TSource>(IObservable<TSource> source, IObservable<MidiSequence> sequence)
        {
            return Observable.Create<TSource>(observer =>
            {
                var gate = new object();
                MidiSequence? latest = null;
                MidiSequence? recent = null;
                MidiSequence? engraved = null;
                NotationDrawing? drawing = null;
                var engravedWidth = float.NaN;
                var engravedOptions = (Clef: default(StaffClef), Resolution: 0, StaffSpacing: 0f, History: 0);

                var sequenceObserver = Observer.Create<MidiSequence>(
                    value =>
                    {
                        var history = History;
                        lock (gate)
                        {
                            latest = value;
                            recent = history > 0 ? NotationHistory.Append(recent, value, history) : null;
                        }
                    },
                    observer.OnError);

                var sourceObserver = Observer.Create<TSource>(
                    value =>
                    {
                        if (!Visible) return;
                        MidiSequence? current;
                        lock (gate) current = History > 0 ? recent ?? latest : latest;
                        if (current == null)
                        {
                            ImGui.TextDisabled("No sequence");
                            observer.OnNext(value);
                            return;
                        }

                        // Engraving is only repeated when the sequence, the layout or the width changes
                        var width = ImGui.GetContentRegionAvail().X;
                        var options = (Clef, Resolution, StaffSpacing, History);
                        if (current != engraved || options != engravedOptions || width != engravedWidth || drawing == null)
                        {
                            var notationOptions = new NotationOptions
                            {
                                Clef = options.Clef,
                                Resolution = options.Resolution,
                                StaffSpacing = options.StaffSpacing
                            };

                            // Trim again in case the history was shortened since the last sequence
                            var visible = options.History > 0 ? NotationHistory.Trim(current, options.History) : current;
                            drawing = NotationEngraver.Engrave(visible, notationOptions, width);
                            engraved = current;
                            engravedOptions = options;
                            engravedWidth = width;
                        }

                        Draw(drawing);
                        observer.OnNext(value);
                    },
                    observer.OnError,
                    observer.OnCompleted);

                return new System.Reactive.Disposables.CompositeDisposable(
                    sequence.SubscribeSafe(sequenceObserver),
                    source.SubscribeSafe(sourceObserver));
            });
        }

        static unsafe void Draw(NotationDrawing drawing)
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var color = ImGui.GetColorU32(ImGuiCol.Text);
            var font = ImGui.GetFont();
            var screenPoints = new Vector2[drawing.Primitives.Max(primitive => primitive.Points.Length)];
            foreach (var primitive in drawing.Primitives)
            {
                var points = primitive.Points;
                switch (primitive.Kind)
                {
                    case PrimitiveKind.Line:
                        drawList.AddLine(origin + points[0], origin + points[1], color, primitive.Thickness);
                        break;
                    case PrimitiveKind.Polyline:
                        for (int i = 0; i < points.Length; i++) screenPoints[i] = origin + points[i];
                        drawList.AddPolyline(ref screenPoints[0], points.Length, color, ImDrawFlags.None, primitive.Thickness);
                        break;
                    case PrimitiveKind.Polygon:
                        for (int i = 0; i < points.Length; i++) screenPoints[i] = origin + points[i];
                        drawList.AddConvexPolyFilled(ref screenPoints[0], points.Length, color);
                        break;
                    case PrimitiveKind.Ellipse:
                        if (primitive.Filled) drawList.AddEllipseFilled(origin + points[0], points[1], color, primitive.Rotation, 24);
                        else drawList.AddEllipse(origin + points[0], points[1], color, primitive.Rotation, 24, primitive.Thickness);
                        break;
                    case PrimitiveKind.Circle:
                        drawList.AddCircleFilled(origin + points[0], points[1].X, color, 16);
                        break;
                    case PrimitiveKind.Bezier:
                        drawList.AddBezierCubic(origin + points[0], origin + points[1], origin + points[2], origin + points[3], color, primitive.Thickness, 16);
                        break;
                    case PrimitiveKind.Text:
                        var text = primitive.Text ?? string.Empty;
                        var size = ImGui.CalcTextSizeA(font, primitive.Size, float.MaxValue, 0, text);
                        drawList.AddText(font.Handle, primitive.Size, origin + points[0] - size / 2, color, text);
                        break;
                }
            }

            // Reserve the space covered by the drawing so the window layout and scrolling account for it
            ImGui.Dummy(new Vector2(drawing.Width, drawing.Height));
        }
    }
}
