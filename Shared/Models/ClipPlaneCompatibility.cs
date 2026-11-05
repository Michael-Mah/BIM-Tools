using Autodesk.Navisworks.Api;

namespace BIM.Tools.Models
{
    // Navisworks 2024 exposes the native section box through the older
    // interop wrapper. Navisworks 2025+ exposes the managed ClipPlaneSet API.
    // Keep the version fork in one place so the production code and native
    // probes exercise the same behavior.
    internal static class ClipPlaneCompatibility
    {
#if NAVISWORKS2024
        internal static bool IsEnabled(Viewpoint viewpoint)
        {
            return viewpoint != null && viewpoint.InternalClipPlanes != null && viewpoint.InternalClipPlanes.IsEnabled();
        }

        internal static bool IsBox(Viewpoint viewpoint)
        {
            return viewpoint != null && viewpoint.InternalClipPlanes != null &&
                   viewpoint.InternalClipPlanes.GetMode() == Autodesk.Navisworks.Api.Interop.LcOaClipPlaneSetMode.eMODE_BOX;
        }

        internal static void GetOrientedBox(Viewpoint viewpoint, BoundingBox3D bounds, Rotation3D rotation)
        {
            viewpoint.InternalClipPlanes.GetOrientedBox(bounds, rotation);
        }

        internal static void ConfigureOrientedBox(Viewpoint viewpoint, BoundingBox3D bounds, Rotation3D rotation)
        {
            viewpoint.InternalClipPlanes.SetOrientedBox(bounds, rotation);
            viewpoint.InternalClipPlanes.SetMode(Autodesk.Navisworks.Api.Interop.LcOaClipPlaneSetMode.eMODE_BOX);
            viewpoint.InternalClipPlanes.SetEnabled(true);
        }

        internal static void SetEnabled(Viewpoint viewpoint, bool enabled)
        {
            viewpoint.InternalClipPlanes.SetEnabled(enabled);
        }
#else
        internal static bool IsEnabled(Viewpoint viewpoint)
        {
            return viewpoint != null && viewpoint.ClipPlanes != null && viewpoint.ClipPlanes.Enabled;
        }

        internal static bool IsBox(Viewpoint viewpoint)
        {
            return viewpoint != null && viewpoint.ClipPlanes != null && viewpoint.ClipPlanes.Mode == ClipPlaneSetMode.Box;
        }

        internal static void GetOrientedBox(Viewpoint viewpoint, BoundingBox3D bounds, Rotation3D rotation)
        {
            viewpoint.ClipPlanes.GetOrientedBox(bounds, rotation);
        }

        internal static void ConfigureOrientedBox(Viewpoint viewpoint, BoundingBox3D bounds, Rotation3D rotation)
        {
            ClipPlaneSet clipping = new ClipPlaneSet();
            clipping.SetOrientedBox(bounds, rotation);
            clipping.Mode = ClipPlaneSetMode.Box;
            clipping.Enabled = true;
            viewpoint.ClipPlanes = clipping;
        }

        internal static void SetEnabled(Viewpoint viewpoint, bool enabled)
        {
            ClipPlaneSet clipping = viewpoint.ClipPlanes;
            clipping.Enabled = enabled;
            viewpoint.ClipPlanes = clipping;
        }
#endif
    }
}
