# AGENTS.md

Instructions for AI coding agents (and humans) working in this repo.

## What this project is

**RemoteTechMapFix** is a small KSP 1.12 mod that fixes RemoteTech's zoomed-out (2D) map
rendering. It raises the far clip of KSP's 2D vector camera, so lines don't vanish on tall
screens. It also replaces RT's 2D line/cone mesh building with near-plane clipping, so they don't
flip 180°. It also hides and cleans up stale line/cone meshes so no "phantom cones" appear.
`README.md` explains all three bugs, the root causes, and the fixes. Read it first.

It's a sibling of `../MechJebRTBridge` (same build setup and conventions) and works around a bug
in `../RemoteTech`. The game runs the **released** RemoteTech 1.9.12 DLL. Line references to RT
should name the version.

## Rule #1: keep the docs in sync with the code

Every change to behaviour, scope, build, or supported versions updates `README.md` (and the
`MapFixAddon` class comment) in the same change. In particular:
- If the root-cause analysis changes (new KSP/RT version, different depth, different camera,
  different RT mesh code), update the README "Why it happens" sections and the class comments
  (`MapFixAddon`, `Clip2DLines`, `StaleMeshFix`) together.
- `Clip2DLines.ConePrefix` copies RT's cone geometry, and `StaleMeshFix.ConesPostfix` copies RT's
  cone antenna selection. If RT changes either, keep them identical.
- If upstream RemoteTech fixes the bug, say which version in README "Upstream", and make the mod a
  no-op or retire it.
- Comments describe the code as it is, not its history.

## Layout

```
src/MapFixAddon.cs          KSPAddon: applies the Harmony patches once; keeps MapView.vectorCam.farClipPlane deep enough
src/Clip2DLines.cs          Harmony prefixes on RT NetworkLine/NetworkCone.UpdateMesh (2D mode only)
src/StaleMeshFix.cs         hides cones RT skipped; destroys line/cone GameObjects RT orphaned
GameData/RemoteTechMapFix/  packaging root (Plugins/ and .version are build outputs, gitignored)
```

## Build & test

- `dotnet build RemoteTechMapFix.csproj -c Release`. Machine paths are in the gitignored
  `Directory.Build.props.user`. The post-build step copies the DLL into
  `$(KSPBT_GameRoot)/GameData/RemoteTechMapFix/Plugins/`.
- No automated tests. To check in game: on a screen taller than about 2000 px (or a window that
  tall), open the tracking station and zoom far out. The RT lines should stay visible, and
  `KSP.log` should show one `[RT MapFix] Raising map vector camera far clip …` line. For the
  flip: target a vessel with a dish and zoom in slowly toward it. The cone and lines should never
  point the wrong way. Startup should log `[RT MapFix] patched RemoteTech.UI.NetworkLine:UpdateMesh`
  and `…NetworkCone:UpdateMesh`, plus `UpdateNetworkCones` and both `Instantiate` patches. For
  phantom cones: give a dish the "Active Vessel" target, focus Kerbin in the tracking station, and
  zoom out past the 2D switch. No cones should appear away from real vessels.
- Versioning comes from `v*.*.*` git tags via MinVer (`MinVerTagPrefix=v`). Don't set `<Version>`
  on the `KSPVersionFile` item. KSPBuildTools fills it from `$(FileVersion)`.
- CI: `release.yml` runs on every branch push, PR and `v*.*.*` tag. Its `build` job compiles
  against the **CKAN-released** RemoteTech/Harmony2, not your local install (pinned to
  `ubuntu-24.04` because CKAN's `.deb` needs `mono-runtime`). The `release` job calls the shared
  `MinmusMachineWorks/KSP-Mods-CKAN/.github/workflows/release-mod.yml`, which packages the zip,
  validates the `.ckan`, and on a tag creates the GitHub release and publishes
  `RemoteTechMapFix/RemoteTechMapFix-X.Y.Z.ckan` (org secret `CKAN_META_TOKEN`). It matches
  `../MechJebRTBridge`'s `release.yml` apart from names and dependencies; keep the two in step.
- CKAN metadata lives in `ckan/metadata.json`. Change dependencies there **and** in the
  `install-dependencies` step of `release.yml`.
