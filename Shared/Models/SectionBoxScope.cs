using System;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Models
{
    /// <summary>
    /// Immutable snapshot of the active native Navisworks oriented Section Box.
    /// World points are transformed into the box's unrotated local coordinates.
    /// </summary>
    public sealed class SectionBoxScope
    {
        private readonly double _qx;
        private readonly double _qy;
        private readonly double _qz;
        private readonly double _qw;

        private SectionBoxScope(
            Point3D minimum,
            Point3D maximum,
            Rotation3D rotation,
            string rawJson)
        {
            Minimum = minimum;
            Maximum = maximum;
            Center = new Point3D(
                (minimum.X + maximum.X) * 0.5,
                (minimum.Y + maximum.Y) * 0.5,
                (minimum.Z + maximum.Z) * 0.5);
            RawJson = rawJson;

            Rotation3D normalized = rotation.Normalize();
            _qx = normalized.A;
            _qy = normalized.B;
            _qz = normalized.C;
            _qw = normalized.D;
        }

        public Point3D Minimum { get; private set; }

        public Point3D Maximum { get; private set; }

        public Point3D Center { get; private set; }

        public string RawJson { get; private set; }

        // A saved clash setup can reconstruct its scope without changing the
        // user's active clipping planes or viewpoint.
        internal static SectionBoxScope FromBox(Point3D minimum, Point3D maximum, Rotation3D rotation, string rawJson)
        { return new SectionBoxScope(minimum, maximum, rotation, rawJson); }

        public static bool TryRead(View view, out SectionBoxScope scope, out string reason)
        {
            scope = null;
            reason = string.Empty;
            if (view == null)
            {
                reason = "No active 3D view is available.";
                return false;
            }

            try
            {
                Viewpoint viewpoint = view.CreateViewpointCopy();
                ClipPlaneSet clipping = viewpoint.ClipPlanes;
                if (clipping == null || !clipping.Enabled)
                {
                    reason = "The native Section Box is disabled.";
                    return false;
                }
                if (clipping.Mode != ClipPlaneSetMode.Box)
                {
                    reason = "The active clipping mode is not a native Section Box.";
                    return false;
                }

                BoundingBox3D orientedBox = new BoundingBox3D();
                Rotation3D rotation = new Rotation3D();
                clipping.GetOrientedBox(orientedBox, rotation);
                Point3D first = orientedBox.Min;
                Point3D second = orientedBox.Max;
                Point3D minimum = new Point3D(
                    Math.Min(first.X, second.X),
                    Math.Min(first.Y, second.Y),
                    Math.Min(first.Z, second.Z));
                Point3D maximum = new Point3D(
                    Math.Max(first.X, second.X),
                    Math.Max(first.Y, second.Y),
                    Math.Max(first.Z, second.Z));

                scope = new SectionBoxScope(
                    minimum,
                    maximum,
                    rotation,
                    view.GetClippingPlanes());
                return true;
            }
            catch (Exception exception)
            {
                reason = "Section Box data could not be read: " + exception.Message;
                return false;
            }
        }

        public bool Contains(Point3D worldPoint)
        {
            Point3D local = ToLocal(worldPoint);
            const double epsilon = 1.0e-7;
            return local.X >= Minimum.X - epsilon && local.X <= Maximum.X + epsilon &&
                   local.Y >= Minimum.Y - epsilon && local.Y <= Maximum.Y + epsilon &&
                   local.Z >= Minimum.Z - epsilon && local.Z <= Maximum.Z + epsilon;
        }

        public bool Intersects(BoundingBox3D worldBounds)
        {
            if (worldBounds == null || worldBounds.IsEmpty)
            {
                return false;
            }

            // Conservative AABB-versus-OBB test. Separation on any of the box's
            // three local axes is definitive; overlap may intentionally include
            // a few extra candidates, which the exact result filter removes.
            Point3D localCenter = ToLocal(worldBounds.Center);
            Vector3D half = worldBounds.Size.Multiply(0.5);
            double[,] axes = GetWorldAxes();
            double[] local = { localCenter.X, localCenter.Y, localCenter.Z };
            double[] minimum = { Minimum.X, Minimum.Y, Minimum.Z };
            double[] maximum = { Maximum.X, Maximum.Y, Maximum.Z };

            for (int axis = 0; axis < 3; axis++)
            {
                double projectedRadius =
                    Math.Abs(axes[axis, 0]) * half.X +
                    Math.Abs(axes[axis, 1]) * half.Y +
                    Math.Abs(axes[axis, 2]) * half.Z;
                if (local[axis] + projectedRadius < minimum[axis] ||
                    local[axis] - projectedRadius > maximum[axis])
                {
                    return false;
                }
            }

            return true;
        }

        public Point3D ToLocal(Point3D world)
        {
            double x = world.X - Center.X;
            double y = world.Y - Center.Y;
            double z = world.Z - Center.Z;

            // Inverse unit quaternion rotation (q conjugate * p * q).
            double tx = 2.0 * (-_qy * z + _qz * y);
            double ty = 2.0 * (-_qz * x + _qx * z);
            double tz = 2.0 * (-_qx * y + _qy * x);
            double lx = x + _qw * tx + (-_qy * tz + _qz * ty);
            double ly = y + _qw * ty + (-_qz * tx + _qx * tz);
            double lz = z + _qw * tz + (-_qx * ty + _qy * tx);
            return new Point3D(lx + Center.X, ly + Center.Y, lz + Center.Z);
        }

        public Point3D ToWorld(Point3D local)
        {
            double x = local.X - Center.X, y = local.Y - Center.Y, z = local.Z - Center.Z;
            double tx = 2.0 * (_qy * z - _qz * y);
            double ty = 2.0 * (_qz * x - _qx * z);
            double tz = 2.0 * (_qx * y - _qy * x);
            return new Point3D(
                x + _qw * tx + (_qy * tz - _qz * ty) + Center.X,
                y + _qw * ty + (_qz * tx - _qx * tz) + Center.Y,
                z + _qw * tz + (_qx * ty - _qy * tx) + Center.Z);
        }

        private double[,] GetWorldAxes()
        {
            // Rows are local box axes expressed in world coordinates.
            double xx = _qx * _qx;
            double yy = _qy * _qy;
            double zz = _qz * _qz;
            double xy = _qx * _qy;
            double xz = _qx * _qz;
            double yz = _qy * _qz;
            double wx = _qw * _qx;
            double wy = _qw * _qy;
            double wz = _qw * _qz;
            return new[,]
            {
                { 1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy) },
                { 2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx) },
                { 2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy) }
            };
        }
    }
}
