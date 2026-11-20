# RemoteTech Map Fix

A tiny KSP 1.12 mod that fixes three bugs in **RemoteTech 1.9.12's zoomed-out map rendering**:

1. Lines and cones **vanish** when zoomed out on tall screens (4K, or any window taller than
   about 2000 px).
2. Lines and cones **flip 180°** while zooming in toward a vessel, just before KSP switches back to
   3D lines.
3. **Phantom cones** (and lines) at meaningless spots, seen when a celestial body is focused and
   the map switches to 2D lines.

Bugs 1 and 2 come from RemoteTech's 2D line mode. KSP uses it when the map camera is farther than
`MapView.max3DlineDrawDist`, or when more orbits are shown than `MAP_MAX_ORBIT_BEFORE_FORCE2D`.

## Bug 1: lines vanish on tall screens

In the map view or tracking station, zoom out far enough and every RemoteTech link line,
active-path line, and dish cone disappears at once. Stock orbit lines stay visible. Zoom back in
and they return. The same install works on a 1440p screen.

### Why it happens

- Past `MapView.max3DlineDrawDist`, KSP switches map lines from 3D to 2D. It removes layer 31
  from the planetarium camera and enables a private orthographic "Canvas Camera"
  (`MapView.vectorCam`) for layer 31. KSP never sets that camera's far clip plane, so it stays at
  Unity's default of **1000**.
- In 2D mode, RemoteTech (`NetworkLine` / `NetworkCone`) writes its mesh vertices in screen pixels
  at depth `Screen.height / 2 + 0.01`.
- At 2160 px tall that depth is about 1080, past the far plane, so everything is clipped. At
  1440 px it's about 720 and renders fine.
- Stock orbit lines are drawn by Vectrosity on its canvas, well inside the clip range, so they
  aren't affected.

This affects RemoteTech 1.9.12 and the current `develop` (Burst rewrite). Both use the same
depth.

### The fix

Every frame, if `MapView.vectorCam.farClipPlane` is closer than RemoteTech's depth, push it out
to `Screen.height / 2 + 100`. It runs every frame because the resolution can change mid-session,
for example when the window moves to another monitor. A log line `[RT MapFix] Raising map vector
camera far clip …` appears whenever it changes the value.

This part touches only stock KSP, via reflection on the private `MapView.vectorCam` field.

## Bug 2: lines and cones flip 180° while zooming in

### Why it happens

In 2D mode RemoteTech projects each segment's endpoints to the screen
(`NetworkLine.UpdateMesh`, `NetworkCone.UpdateMesh`). When an endpoint is behind the camera,
it mirrors it with `NetworkLine.FlipDirection` instead of clipping the segment:
- **Cones:** if *either* cone edge is behind the camera, it mirrors *both* edges. The edge that
  was correct now points the opposite way. When the antenna itself is behind the camera, it
  mirrors the antenna about the target planet rather than about each edge.
- **Lines:** the mirror only approximates the visible part, and it snaps as an endpoint crosses
  the camera plane.

Zooming in one more step switches KSP to 3D lines. RemoteTech's 3D path round-trips the points
exactly, so the image flips back.

### The fix

A Harmony prefix on both `UpdateMesh` methods takes over **2D mode only**. It clips each
segment where it crosses the camera's near plane, projects the visible part, and writes
RemoteTech's own quad layout and depth. 3D mode is left to RemoteTech. If a patch target is
missing (a different RT version), that patch is skipped with a log warning, and the far-clip
fix still applies.

## Bug 3: phantom cones when a body is focused

### Why it happens

Two bugs in RemoteTech's `NetworkRenderer` leave old meshes on screen:
- `UpdateNetworkCones` skips an antenna whose target has no position (`continue`) without hiding
  the cone object at that index. It stays active, still drawing whatever it last drew. A dish
  targeting **"Active Vessel"** hits this whenever there's no active vessel, for example when a
  celestial body is focused in the tracking station. In 3D mode the stale mesh sits at an old
  world position, usually hidden behind the planet. After the switch to 2D, its world coordinates
  are read as screen pixels, so cones float at meaningless spots. Focusing a vessel gives "Active
  Vessel" a position, so no phantoms appear.
- When the line/cone list shrinks, RemoteTech calls `Destroy()` on the line/cone *component*
  rather than its GameObject. The mesh and renderer stay behind, still drawing.

### The fix

A postfix on `UpdateNetworkCones` hides any cone whose antenna target has no position. Every
line/cone GameObject RemoteTech creates is tracked, and any whose component has been destroyed
is destroyed too, on the next frame.

## Requirements

RemoteTech (tested in game with 1.9.12) and Harmony (`000_Harmony`).

## Install

Via CKAN: add the meta repo with
`ckan repo add Machine-Maker-Mods https://github.com/Machine-Maker/KSP-Mods-CKAN/archive/main.tar.gz`,
then `ckan install RemoteTechMapFix`.

Manually: the release zip holds `RemoteTechMapFix-vX.Y.Z/GameData/RemoteTechMapFix/`. Copy that
`GameData/RemoteTechMapFix/` into your KSP `GameData/`.

## Building

Same setup as MechJebRTBridge: KSPBuildTools, plus a gitignored `Directory.Build.props.user`
pointing `KSPBT_GameRoot` (and on Linux `KSPBT_ManagedPath`) at your install.

```
dotnet build RemoteTechMapFix.csproj -c Release
```

The DLL goes to `GameData/RemoteTechMapFix/Plugins/` in the repo, and into the install's
GameData when `KSPBT_GameRoot` is set. Versions come from `v*.*.*` git tags (MinVer).

CI builds every push against the CKAN-released RemoteTech and Harmony2. Pushing a `v*.*.*` tag
runs the release workflow (`.github/workflows/release.yml`). It attaches
`RemoteTechMapFix-vX.Y.Z.zip` and `RemoteTechMapFix.version` to a GitHub release, and commits a
`.ckan` file to the [KSP-Mods-CKAN](https://github.com/Machine-Maker/KSP-Mods-CKAN) meta repo.
That last step needs the `CKAN_META_TOKEN` repo secret: a token with Contents write access to
KSP-Mods-CKAN.

## Upstream

All three fixes belong in RemoteTech. For bug 1, use a depth inside the vector camera's clip range,
or map the 2D points through `vectorCam.ScreenToWorldPoint`. For bug 2, clip at the near plane
instead of `FlipDirection`. For bug 3, deactivate skipped cones and destroy the GameObject, not
the component. The `develop` Burst rewrite keeps the same 2D depth (bug 1). Its
line/cone mesh jobs haven't been checked for bug 2. This mod is a drop-in workaround for the
1.9.12 release.
