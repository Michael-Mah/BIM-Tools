using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using BIM.Tools.Models;

namespace BIM.Tools.Services
{
    /// <summary>Clips primitives against all six planes in the oriented box's local frame.</summary>
    internal sealed class BoxGeometryClipper
    {
        private readonly SectionBoxScope _scope;
        public BoxGeometryClipper(SectionBoxScope scope) { _scope = scope; }

        public IList<Point3D> Triangle(Point3D a, Point3D b, Point3D c)
        {
            List<Point3D> polygon = new List<Point3D> { _scope.ToLocal(a), _scope.ToLocal(b), _scope.ToLocal(c) };
            for (int axis = 0; axis < 3 && polygon.Count != 0; axis++)
            {
                polygon = Cut(polygon, axis, Value(_scope.Minimum, axis), true);
                polygon = Cut(polygon, axis, Value(_scope.Maximum, axis), false);
            }
            for (int i = 0; i < polygon.Count; i++) polygon[i] = _scope.ToWorld(Clamp(polygon[i]));
            return polygon;
        }

        public bool Line(Point3D a, Point3D b, out Point3D first, out Point3D last)
        {
            a = _scope.ToLocal(a); b = _scope.ToLocal(b);
            double low = 0, high = 1;
            first = last = null;
            for (int axis = 0; axis < 3; axis++)
            {
                double start = Value(a, axis), delta = Value(b, axis) - start;
                double min = Value(_scope.Minimum, axis), max = Value(_scope.Maximum, axis);
                if (Math.Abs(delta) < 1e-20) { if (start < min || start > max) return false; continue; }
                double enter = (min - start) / delta, exit = (max - start) / delta;
                if (enter > exit) { double swap = enter; enter = exit; exit = swap; }
                low = Math.Max(low, enter); high = Math.Min(high, exit);
                if (low > high) return false;
            }
            first = _scope.ToWorld(Clamp(Lerp(a, b, low)));
            last = _scope.ToWorld(Clamp(Lerp(a, b, high)));
            return true;
        }

        private List<Point3D> Cut(List<Point3D> input, int axis, double boundary, bool minimum)
        {
            List<Point3D> output = new List<Point3D>();
            if (input.Count == 0) return output;
            Point3D previous = input[input.Count - 1];
            bool before = minimum ? Value(previous, axis) >= boundary : Value(previous, axis) <= boundary;
            foreach (Point3D current in input)
            {
                bool inside = minimum ? Value(current, axis) >= boundary : Value(current, axis) <= boundary;
                if (inside != before)
                {
                    double fraction = (boundary - Value(previous, axis)) / (Value(current, axis) - Value(previous, axis));
                    output.Add(Lerp(previous, current, fraction));
                }
                if (inside) output.Add(current);
                previous = current; before = inside;
            }
            return output;
        }

        private Point3D Clamp(Point3D p)
        {
            return new Point3D(Math.Max(_scope.Minimum.X, Math.Min(_scope.Maximum.X, p.X)),
                Math.Max(_scope.Minimum.Y, Math.Min(_scope.Maximum.Y, p.Y)),
                Math.Max(_scope.Minimum.Z, Math.Min(_scope.Maximum.Z, p.Z)));
        }
        private static double Value(Point3D p, int axis) { return axis == 0 ? p.X : axis == 1 ? p.Y : p.Z; }
        private static Point3D Lerp(Point3D a, Point3D b, double t)
        { return new Point3D(a.X + (b.X-a.X)*t, a.Y + (b.Y-a.Y)*t, a.Z + (b.Z-a.Z)*t); }
    }
}
