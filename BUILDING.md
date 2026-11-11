# Build and validation

## Unity project

Open the repository root in **Unity 6000.3.22f1** with Android Build Support, SDK/NDK and OpenJDK. `Assets`, `Packages` and `ProjectSettings` are included; generated caches and builds are not.

1. Restore the manifest/lockfile dependencies: URP 17.3, AR Foundation 6.6.2, Unity OpenXR: Meta 2.6.1, OpenXR 1.18 and the Input System. Additional upstream sample dependencies remain in the manifest.
2. Open `Assets/DeskSpace/DeskSpace.unity`.
3. Use **Portfolio 2 → Demo 6 — Prepare desk planner** and **Build Quest APK**. The Editor menu retains its original development name; the application is DeskSpace.

Equivalent batch build:

```sh
Unity -batchmode -quit -projectPath "$PWD" -buildTarget Android \
  -executeMethod DeskSpaceBuilder.BuildApk
```

Use the Unity executable appropriate to your installation. Output: `Builds/DeskSpace-v0.1.2.apk`. Android ARM64/IL2CPP, Vulkan/OpenXR, min API 32, target API 36, version code 3; package `com.danielaguilar.deskspace`.

## Device behavior and permissions

- Spatial data supports live depth, tabletop hits, anchors and occlusion. Raw headset-camera images and microphone access are not requested by the planner.
- Begin with a clear, well-lit real desk. Complete headset environment/Space Setup if requested by the runtime.
- The planner removes the upstream sample's unused saved-room readers and marker-camera path from its generated scene; those reference sources remain in the repository with attribution.
- The app retains a **real live anchor**, but no Save/Load or cross-session restoration. Any legacy preferences/anchors are left untouched rather than migrated or erased.
- New desk uses two-step confirmation. Ordinary help, panel movement and cancelling an edit should not clear the arrangement.

## Validation retained

The preparation method runs **26 checks** for measurement, guided-point validity, boundary fitting, rotated overlap, invalid poses, the session-only lifetime notice and retained live anchor/depth managers. Editor preview tools cover onboarding/layout states without invoking a real anchor service. `Validation/` contains synthetic desktop captures; `docs/images/` contains actual walkthrough frames.

Real Quest feedback confirmed general operation/occlusion and basic live alignment while moving the head. No numeric drift study, certified object-fit result or sustained device benchmark is claimed. The public packaging does not substitute for retesting after code, package or runtime changes.

## Published binary

The release reuses the previously tested APK without rebuilding:

| Version | Bytes | SHA-256 |
| --- | ---: | --- |
| 0.1.2 (3) | 36,061,013 | `79f86832f78b0aa0434c5c61fd2f22cfdb14e9f1f40511436e3063b8dfc153ec` |

The publication date is 11 November 2026; the existing device-tested build is unchanged.
