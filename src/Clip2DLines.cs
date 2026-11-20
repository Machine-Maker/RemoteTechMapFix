using System;
using HarmonyLib;
using RemoteTech;
using RemoteTech.SimpleTypes;
using RemoteTech.UI;
using UnityEngine;

namespace RemoteTechMapFix
{
    /// <summary>
    /// Replaces RemoteTech's 2D-mode (zoomed-out map) mesh building for network lines and dish
    /// cones with one that clips each segment at the camera's near plane before projecting it.
    ///
    /// RemoteTech 1.9.12 (<c>NetworkLine.UpdateMesh</c>, <c>NetworkCone.UpdateMesh</c>)
    /// projects both endpoints to the screen and, if one is behind the camera, mirrors it with
    /// <c>NetworkLine.FlipDirection</c> instead of clipping. That only approximates the visible
    /// part, and the cone version is wrong: when either cone edge is behind the camera it
    /// mirrors <em>both</em> edges, so the edge that was fine points 180° the wrong way. It also
    /// mirrors the antenna about the target planet when the antenna is behind the camera.
    /// Zooming in one more step switches KSP to 3D lines, which RemoteTech rebuilds exactly, so
    /// the image flips back.
    ///
    /// The 3D path is left to RemoteTech, which is correct there: WorldToScreenPoint followed by
    /// ScreenToWorldPoint at the same depth is an exact round trip even behind the camera.
    ///
    /// Patched from <see cref="MapFixAddon"/>:
    ///   - LinePrefix → prefix on RemoteTech.UI.NetworkLine.UpdateMesh(BidirectionalEdge&lt;ISatellite&gt;)
    ///   - ConePrefix → prefix on RemoteTech.UI.NetworkCone.UpdateMesh(Vector3d, IAntenna)
    /// Each returns false (skip RemoteTech's body) only in 2D mode, after writing the mesh itself.
    /// </summary>
    public static class Clip2DLines
    {
        private static readonly AccessTools.FieldRef<NetworkLine, MeshFilter> LineMesh =
            AccessTools.FieldRefAccess<NetworkLine, MeshFilter>("mMeshFilter");
        private static readonly AccessTools.FieldRef<NetworkCone, MeshFilter> ConeMesh =
            AccessTools.FieldRefAccess<NetworkCone, MeshFilter>("mMeshFilter");

        private static readonly Vector3[] LineVerts = new Vector3[4];
        private static readonly Vector3[] ConeVerts = new Vector3[8];

        // ---- Harmony prefixes ----------------------------------------------

        internal static bool LinePrefix(NetworkLine __instance, BidirectionalEdge<ISatellite> edge)
        {
            if (MapView.Draw3DLines) return true;
            try
            {
                Camera cam = PlanetariumCamera.Camera;
                Vector3d a = ScaledSpace.LocalToScaledSpace(edge.A.Position);
                Vector3d b = ScaledSpace.LocalToScaledSpace(edge.B.Position);

                WriteSegment(cam, a, b, __instance.LineWidth, LineVerts, 0);
                Apply(LineMesh(__instance).mesh, LineVerts);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[RT MapFix] NetworkLine 2D clip failed, using RemoteTech's: " + e);
                return true;
            }
        }

        internal static bool ConePrefix(NetworkCone __instance, Vector3d center, IAntenna dish)
        {
            if (MapView.Draw3DLines) return true;
            try
            {
                Camera cam = PlanetariumCamera.Camera;

                // Cone geometry, same as RemoteTech's NetworkCone.UpdateMesh.
                Vector3d antennaPos = ScaledSpace.LocalToScaledSpace(RTCore.Instance.Network[dish.Guid].Position);
                Vector3d planetPos  = ScaledSpace.LocalToScaledSpace(center);

                CelestialBody refFrame = MapView.MapCamera.target.vessel != null
                    ? MapView.MapCamera.target.vessel.mainBody
                    : MapView.MapCamera.target.celestialBody;
                Vector3 up = refFrame != null ? refFrame.transform.up : Vector3.up;

                float dist = Vector3.Distance(antennaPos, planetPos);
                Vector3 space = Vector3.Cross(planetPos - antennaPos, up).normalized
                                * dist * (float)Math.Tan(Math.Acos(dish.CosAngle));
                double len = Math.Min(dish.Dish / ScaledSpace.ScaleFactor, dist);
                Vector3d end1 = antennaPos + (planetPos + space - antennaPos).normalized * len;
                Vector3d end2 = antennaPos + (planetPos - space - antennaPos).normalized * len;

                WriteSegment(cam, antennaPos, end1, __instance.LineWidth, ConeVerts, 0);
                WriteSegment(cam, antennaPos, end2, __instance.LineWidth, ConeVerts, 4);
                Apply(ConeMesh(__instance).mesh, ConeVerts);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[RT MapFix] NetworkCone 2D clip failed, using RemoteTech's: " + e);
                return true;
            }
        }

        // ---- geometry ------------------------------------------------------

        /// <summary>
        /// Clips world segment a→b to the part in front of the camera's near plane, projects it,
        /// and writes RemoteTech's 4-vertex quad layout (start−n, start+n, end−n, end+n) in screen
        /// pixels at RemoteTech's 2D depth. A segment entirely behind the camera collapses to a
        /// zero-area quad.
        /// </summary>
        private static void WriteSegment(Camera cam, Vector3d a, Vector3d b, float width,
                                         Vector3[] verts, int offset)
        {
            Vector3d camPos = cam.transform.position;
            Vector3d fwd    = cam.transform.forward;
            double near     = cam.nearClipPlane + 0.01;

            double da = Vector3d.Dot(a - camPos, fwd);
            double db = Vector3d.Dot(b - camPos, fwd);

            if (da < near && db < near)
            {
                for (int i = 0; i < 4; i++) verts[offset + i] = Vector3.zero;
                return;
            }
            if (da < near) a = a + (b - a) * ((near - da) / (db - da));
            else if (db < near) b = b + (a - b) * ((near - db) / (da - db));

            Vector3 s = cam.WorldToScreenPoint(a);
            Vector3 e = cam.WorldToScreenPoint(b);
            Vector3 n = new Vector3(e.y - s.y, s.x - e.x, 0f).normalized * (width / 2f);

            // RemoteTech's 2D depth; MapFixAddon keeps the vector camera's far plane beyond it.
            float z = Screen.height / 2f + 0.01f;
            s.z = z;
            e.z = z;

            verts[offset + 0] = s - n;
            verts[offset + 1] = s + n;
            verts[offset + 2] = e - n;
            verts[offset + 3] = e + n;
        }

        private static void Apply(Mesh mesh, Vector3[] verts)
        {
            mesh.vertices = verts;
            // Clipped endpoints can land far off-screen; use RemoteTech's generous 2D bounds so
            // the mesh is never culled while part of it is on screen.
            mesh.bounds = new Bounds(
                new Vector3(Screen.width / 2f, Screen.height / 2f, Screen.height / 2f),
                new Vector3(Screen.width * 200f, Screen.height * 200f, 0.2f));
            mesh.MarkDynamic();
        }
    }
}
