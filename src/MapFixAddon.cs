using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RemoteTechMapFix
{
    /// <summary>
    /// Entry point. Applies the RemoteTech renderer patches once (see <see cref="Clip2DLines"/>,
    /// <see cref="StaleMeshFix"/>), sweeps orphaned line/cone GameObjects each frame, and keeps
    /// RemoteTech's map-view network lines and cones visible on tall screens (4K etc.):
    ///
    /// When the map camera zooms out past <c>MapView.max3DlineDrawDist</c>, KSP stops drawing map
    /// lines in 3D. It takes layer 31 off the planetarium camera and enables a private orthographic
    /// "Canvas Camera" (<c>MapView.vectorCam</c>) for layer 31 instead. That camera keeps Unity's
    /// default far clip plane of 1000.
    ///
    /// In that 2D mode RemoteTech (NetworkLine / NetworkCone, 1.9.12 and the develop rewrite)
    /// writes its mesh vertices in screen pixels at depth <c>Screen.height / 2 + 0.01</c>. Once
    /// the screen is taller than about 2000 px that depth passes the far plane, and every line
    /// and cone is clipped at once. Stock orbit lines are unaffected: Vectrosity draws them on
    /// its canvas, well inside the clip range.
    ///
    /// Fix: whenever the vector camera's far plane is closer than RemoteTech's depth, push it
    /// out. This is checked every frame because the resolution can change mid-session (moving
    /// the window to another monitor, resizing).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class MapFixAddon : MonoBehaviour
    {
        private const string Tag = "[RT MapFix]";

        // Headroom beyond RemoteTech's depth. The camera sits at z = -1 relative to its canvas,
        // and Screen.height can change mid-session.
        private const float Margin = 100f;

        private static readonly FieldInfo VectorCamField =
            typeof(MapView).GetField("vectorCam", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool _warnedMissingField;
        private static bool _patched;

        public void Start()
        {
            if (!_patched)
            {
                _patched = true;
                PatchAll();
            }

            if (VectorCamField == null && !_warnedMissingField)
            {
                _warnedMissingField = true;
                Debug.LogWarning($"{Tag} MapView.vectorCam not found in this KSP version; fix inactive.");
            }
        }

        // RemoteTech renderer patches. Each patch is isolated, so a RemoteTech version with a
        // different signature only loses that one patch.
        private static void PatchAll()
        {
            var harmony = new Harmony("com.remotetechmapfix");

            // Near-plane clipping for 2D lines/cones (see Clip2DLines).
            Patch(harmony, "RemoteTech.UI.NetworkLine:UpdateMesh",
                  prefix: H(typeof(Clip2DLines), nameof(Clip2DLines.LinePrefix)));
            Patch(harmony, "RemoteTech.UI.NetworkCone:UpdateMesh",
                  prefix: H(typeof(Clip2DLines), nameof(Clip2DLines.ConePrefix)));

            // Stale/orphaned line and cone meshes (see StaleMeshFix).
            Patch(harmony, "RemoteTech.NetworkRenderer:UpdateNetworkCones",
                  postfix: H(typeof(StaleMeshFix), nameof(StaleMeshFix.ConesPostfix)));
            Patch(harmony, "RemoteTech.UI.NetworkLine:Instantiate",
                  postfix: H(typeof(StaleMeshFix), nameof(StaleMeshFix.TrackLinePostfix)));
            Patch(harmony, "RemoteTech.UI.NetworkCone:Instantiate",
                  postfix: H(typeof(StaleMeshFix), nameof(StaleMeshFix.TrackConePostfix)));
        }

        private static HarmonyMethod H(System.Type t, string name) => new HarmonyMethod(t, name);

        private static void Patch(Harmony harmony, string target,
                                  HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                MethodBase method = AccessTools.Method(target);
                if (method == null)
                {
                    Debug.LogWarning($"{Tag} patch target not found (skipped): {target}");
                    return;
                }
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                Debug.Log($"{Tag} patched {target}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{Tag} failed to patch {target} (skipped): {e.Message}");
            }
        }

        public void LateUpdate()
        {
            StaleMeshFix.SweepOrphans();

            if (VectorCamField == null) return;

            MapView map = MapView.fetch;
            if (map == null) return;

            var cam = VectorCamField.GetValue(map) as Camera;
            if (cam == null) return;

            // RemoteTech's 2D depth is Screen.height/2 + 0.01 (NetworkLine/NetworkCone.UpdateMesh).
            float needed = Screen.height * 0.5f + Margin;
            if (cam.farClipPlane >= needed) return;

            Debug.Log($"{Tag} Raising map vector camera far clip {cam.farClipPlane:F0} → {needed:F0} " +
                      $"(screen {Screen.width}x{Screen.height}).");
            cam.farClipPlane = needed;
        }
    }
}
