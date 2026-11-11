# Mixed Reality Example (CSCI 4830/6830, Fall 2026)

A small mixed-reality sandbox for Meta Quest 3 / 3S built on **AR Foundation** and **Unity OpenXR: Meta**
(no Meta SDK). Lecture 8, Mixed reality.

- **Try it without building:** [MRDemo.apk](https://vr26.vn.ugavel.com/lecture8/MRDemo.apk) (install with
  `adb install -r MRDemo.apk`, or with Meta Quest Developer Hub)
- **The same ideas in a web page:** [Mixed reality anywhere](https://vr26.vn.ugavel.com/examples/mixed-reality/)
  (WebXR: Quest, Android phones, Galaxy XR; view the page source for the code)
- **AprilTags to print:** [`apriltags-41h12-ids0-3.pdf`](apriltags-41h12-ids0-3.pdf) (print at 100%: the inner white square is 100 mm)

## What it does

A demo about **anchors**: three ways of pinning virtual things to the real room, side by side.

| What you do | What happens |
|---|---|
| Pinch or trigger, pointing at a table or the floor | A cactus on a **spatial anchor**, saved: green ring. Next launch it comes back on its own: blue ring |
| Put a printed AprilTag in view (`apriltags-41h12-ids0-3.pdf`, print at 100%) | A **marker anchor**: a purple frame on the tag and a shape on top. It follows the tag while in view and sits on its anchor when the tag is out of view |
| Look around | **Room anchors** from Space Setup: planes and furniture boxes, labelled (Room button) |

The panel above your left hand shows what the headset gives an app, live: both front passthrough cameras (what the
tag detector sees, each found tag outlined by its four detected corners) and Meta's environment depth map for the left
eye (near = warm, far = cool, black = no measurement; only while occlusion is on). Below that: what each colour means,
and live numbers: camera fps, detection rate and time, and tag jitter.
Its five buttons work with a pinch (hands) or the trigger (controllers); controllers have shortcuts too:

| Button | Does |
|---|---|
| Occlusion (A) | off / hard / soft: real things (your hand) hide the virtual ones |
| Depth bias (right thumbstick click) | none / low / medium / high: how much closer a real surface must be before it hides a virtual one (below) |
| Room (B) | show or hide the planes and furniture boxes |
| Stereo (X) | find tags in both front cameras and triangulate, or use one camera; compare the jitter readout |
| Clear all (Y) | remove every cactus and tag anchor and erase the saved anchors |

**Depth bias.** Meta's depth map is coarse and its error grows quickly with distance (stereo depth error grows with
distance squared). Without slack, a cactus standing on a table flickers into the table, and far-away walls nibble
at anything near them. So before the depth test, each virtual surface is treated as bias(z) = a + k·z² metres closer
than it is: medium (the default) is 6 cm at 1 m, 18 cm at 2 m, 38 cm at 3 m. The levels are in `MRSandbox.cs`; the
shader side is `BiasSceneDepth` in `Shaders/Occlusion/OcclusionComputation.hlsl`. The WebXR page uses the same levels.

The room comes from **Space Setup** (Settings > Environment Setup > Space Setup). Run it once before the app.
The app asks for two permissions: *spatial data* (`com.oculus.permission.USE_SCENE`: planes, boxes, mesh, depth)
and *headset cameras* (`horizonos.permission.HEADSET_CAMERA`: camera frames).

## Versions

Unity 6000.3, URP 17.3, AR Foundation 6.6, Unity OpenXR: Meta 2.6 (`com.unity.xr.meta-openxr`), OpenXR 1.18,
Input System 1.20, AprilTag 1.0.3 (Keijiro Takahashi, BSD-2), glTFast 6.20. Android, Vulkan only (occlusion needs it), minimum API 32 (camera images need it).

## Build

Everything (project settings, OpenXR features, scene, APK) is made by `Assets/MRDemo/Editor/MRDemoBuilder.cs`:

- Menu: **MR Demo > Set Up Project**, **MR Demo > Build Scene**, **MR Demo > Build APK**
- Batch: `Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod MRDemoBuilder.BuildApk`
  → `Builds/MRDemo.apk`, then `adb install -r Builds/MRDemo.apk`

## Where things are

- `Assets/MRDemo/Scripts`
  - `MRSandbox.cs`: the rules (place a cactus on a saved anchor, reload saved anchors, occlusion modes, depth bias)
  - `HandRay.cs`, `MRInput.cs`: one pointer per hand, controllers or tracked hands
  - `SceneLook.cs`: colors and labels planes and furniture boxes by classification
  - `TagAnchors.cs`: AprilTag marker anchors from the passthrough cameras (luma plane only, search windows around
    tracked tags, two worker threads, stereo triangulation), plus the live camera preview
  - `WorldButton.cs`: panel buttons you point at
  - `PermissionGate.cs`: asks for the permissions and starts the managers that need them
  - `Hud.cs`: the panel above the left hand
- `Assets/MRDemo/Models/cactus.glb`: the cactus (made with TRELLIS), imported by glTFast; its colour texture is extracted to `cactus_color.png`
- `Assets/MRDemo/Shaders/GrayPreview.shader`: shows a camera's luma plane
- `Assets/MRDemo/Shaders/DepthView.shader`: shows the environment depth texture (`_EnvironmentDepthTexture`, one slice per eye) in colour
- `Assets/MRDemo/Shaders/Occlusion`: occlusion shaders from Unity's AR Foundation Samples (Unity Companion License),
  plus the depth bias. Only materials using these shaders can be hidden by real objects.

## Going further

- Meta's own stack (Meta XR Core SDK + MR Utility Kit) adds QR codes, environment raycasts, scene queries
  and shared anchors with colocation.
- Shared anchors (two headsets, one room) come with the networking lecture.
