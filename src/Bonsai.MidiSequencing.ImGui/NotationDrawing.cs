using System.Collections.Generic;
using System.Numerics;

namespace Bonsai.MidiSequencing.ImGui
{
    enum PrimitiveKind
    {
        Line,
        Polyline,
        Polygon,
        Ellipse,
        Circle,
        Bezier,
        Text
    }

    // A single vector shape in notation coordinates, relative to the top left corner of the drawing
    readonly struct Primitive
    {
        public Primitive(PrimitiveKind kind, Vector2[] points, float thickness = 0, bool filled = true, float rotation = 0, string? text = null, float size = 0)
        {
            Kind = kind;
            Points = points;
            Thickness = thickness;
            Filled = filled;
            Rotation = rotation;
            Text = text;
            Size = size;
        }

        public PrimitiveKind Kind { get; }

        // Line, polyline, polygon and bezier: the control points
        // Ellipse: the center and radii; circle: the center and radius in X; text: the center
        public Vector2[] Points { get; }

        public float Thickness { get; }

        public bool Filled { get; }

        public float Rotation { get; }

        public string? Text { get; }

        public float Size { get; }
    }

    // The recorded shapes of an engraved sequence, which can be replayed on any canvas
    class NotationDrawing
    {
        readonly List<Primitive> primitives = new();

        public IReadOnlyList<Primitive> Primitives => primitives;

        public float Width { get; set; }

        public float Height { get; set; }

        public void Line(Vector2 from, Vector2 to, float thickness)
        {
            primitives.Add(new Primitive(PrimitiveKind.Line, new[] { from, to }, thickness));
        }

        public void Polyline(Vector2[] points, float thickness)
        {
            primitives.Add(new Primitive(PrimitiveKind.Polyline, points, thickness));
        }

        // The polygon must be convex
        public void Polygon(params Vector2[] points)
        {
            primitives.Add(new Primitive(PrimitiveKind.Polygon, points));
        }

        public void Rectangle(Vector2 min, Vector2 max)
        {
            Polygon(min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y));
        }

        public void Ellipse(Vector2 center, Vector2 radius, float rotation, bool filled, float thickness)
        {
            primitives.Add(new Primitive(PrimitiveKind.Ellipse, new[] { center, radius }, thickness, filled, rotation));
        }

        public void Circle(Vector2 center, float radius)
        {
            primitives.Add(new Primitive(PrimitiveKind.Circle, new[] { center, new Vector2(radius) }));
        }

        public void Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float thickness)
        {
            primitives.Add(new Primitive(PrimitiveKind.Bezier, new[] { p0, p1, p2, p3 }, thickness));
        }

        public void Text(Vector2 center, float size, string text)
        {
            primitives.Add(new Primitive(PrimitiveKind.Text, new[] { center }, text: text, size: size));
        }
    }
}
