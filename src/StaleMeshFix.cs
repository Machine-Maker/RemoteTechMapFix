using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RemoteTech;
using RemoteTech.UI;
using UnityEngine;

namespace RemoteTechMapFix
{
    /// <summary>
    /// Stops RemoteTech's map renderer from leaving stale line/cone meshes on screen ("phantom
    /// cones"). Two RemoteTech 1.9.12 bugs in <c>NetworkRenderer</c>:
    ///
    /// 1. <c>UpdateNetworkCones</c> skips an antenna whose target has no position
    ///    (<c>if (!center.HasValue) continue;</c>) without hiding the cone object at that index.
    ///    It stays active with the mesh from whatever antenna it last drew. A dish targeting
    ///    "Active Vessel" hits this whenever there is no active vessel, e.g. a celestial body
    ///    focused in the tracking station. In 3D mode the stale mesh sits at an old world
    ///    position. After the switch to 2D its world coordinates are read as screen pixels, so
    ///    cones float at meaningless spots.
    /// 2. When the line or cone list shrinks it calls <c>Destroy(mLines[i])</c> /
    ///    <c>Destroy(mCones[i])</c> on the <em>component</em>, so the GameObject with its mesh and
    ///    renderer is left behind, still drawing its last geometry.
    ///
    /// Patched from <see cref="MapFixAddon"/>:
    ///   - ConesPostfix   → postfix on RemoteTech.NetworkRenderer.UpdateNetworkCones (hide skipped cones)
    ///   - TrackLinePostfix / TrackConePostfix → postfix on NetworkLine/NetworkCone.Instantiate (record GameObjects)
    ///   - SweepOrphans() → called from MapFixAddon.LateUpdate (destroy GameObjects whose component is gone)
    /// </summary>
    public static class StaleMeshFix
    {
        private static readonly AccessTools.FieldRef<NetworkRenderer, List<NetworkCone>> Cones =
            AccessTools.FieldRefAccess<NetworkRenderer, List<NetworkCone>>("mCones");

        // NetworkManager.GetPositionFromGuid is internal.
        private static readonly MethodInfo GetPositionFromGuid =
            AccessTools.Method(typeof(NetworkManager), "GetPositionFromGuid");

        private static readonly List<GameObject> Tracked = new List<GameObject>();

        // ---- bug 1: cones skipped for a target without a position ----------

        internal static void ConesPostfix(NetworkRenderer __instance)
        {
            try
            {
                List<NetworkCone> cones = Cones(__instance);
                if (cones.Count == 0 || GetPositionFromGuid == null) return;

                // Same selection and order as NetworkRenderer.UpdateNetworkCones.
                List<IAntenna> antennas = RTCore.Instance.Antennas
                    .Where(ant => ant.Powered && ant.CanTarget && RTCore.Instance.Satellites[ant.Guid] != null
                                  && ant.Target != Guid.Empty)
                    .ToList();

                int n = Math.Min(cones.Count, antennas.Count);
                for (int i = 0; i < n; i++)
                {
                    if (cones[i] == null) continue;
                    var center = (Vector3d?)GetPositionFromGuid.Invoke(RTCore.Instance.Network,
                                                                      new object[] { antennas[i].Target });
                    if (!center.HasValue) cones[i].Active = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RT MapFix] cone stale-mesh fix failed: " + e);
            }
        }

        // ---- bug 2: GameObjects orphaned by Destroy(component) -------------

        internal static void TrackLinePostfix(NetworkLine __result) => Track(__result);
        internal static void TrackConePostfix(NetworkCone __result) => Track(__result);

        private static void Track(Component c)
        {
            if (c != null) Tracked.Add(c.gameObject);
        }

        internal static void SweepOrphans()
        {
            for (int i = Tracked.Count - 1; i >= 0; i--)
            {
                GameObject go = Tracked[i];
                if (go == null) { Tracked.RemoveAt(i); continue; }
                if (go.GetComponent<NetworkLine>() != null || go.GetComponent<NetworkCone>() != null) continue;

                UnityEngine.Object.Destroy(go);
                Tracked.RemoveAt(i);
            }
        }
    }
}
