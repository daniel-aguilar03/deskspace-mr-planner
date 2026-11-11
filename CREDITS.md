# Credits and provenance

Developed by **Daniel Aguilar**. The desktop-measurement guide, accessory catalog/models, footprint geometry, selection/editing flow, session-only planner and application-specific revisions are the project's own work.

This standalone edition was extracted from the original development source at commit `a8075cc42b6bfc2387ba3e3dba7d15f7971b8845` on 11 November 2026. It began during the University of Georgia Virtual Reality course. The original submission and history remain unchanged. Daniel confirmed permission to publicly distribute the included example code and built APK on 11 November 2026; this is not represented as a blanket open-source license from the upstream author.

## Foundations and assets

- Dr. Kyle Johnsen / UGA, [MixedRealityExample2026](https://github.com/ugavrclass2026/MixedRealityExample2026), imported at `cc4030dbb9472b1a69d16fca3cee2ea15b99d7d3`: AR Foundation/OpenXR configuration, spatial-anchor/depth examples, controller rays and UI/permission foundations. [Original example README](docs/UPSTREAM_README.md).
- Unity AR Foundation Samples occlusion shaders, copyright 2020 Unity Technologies ApS: the upstream notice is retained in `Assets/MRDemo/Shaders/Occlusion/LICENSE.md`; see [Unity Companion License](licenses/UNITY-COMPANION-LICENSE.md). These assets retain their applicable terms, not a newly assigned MIT license.
- AprilTag and glTFast packages belong to their upstream authors and keep their own licenses. Package manifests/lockfiles are included, not generated package caches.
- The unused reference sandbox's cactus asset and AprilTag PDF remain part of the credited upstream reference project; the generated DeskSpace scene does not use them.
- Equipment models are procedural/stylized previews. The console approximates the original PS5 enclosure, not official Sony CAD, a precision fit guarantee or an endorsement.

## AI assistance

Implementation, geometry checks, debugging and documentation used OpenAI Codex with **GPT 6.1 Sol** as Daniel's reported model label; exact backend revisions were not recorded. Daniel chose the concept and bounded catalog, directed the revisions, performed the headset tests, narrated/recorded the walkthrough and obtained informal playtest feedback.

Local Whisper tiny.en helped recording timing checks; FFmpeg/Python combined actual headset footage, student-camera footage and narration. The video is not synthetic gameplay or a generated student voice.

No new repository-wide license is assigned by this standalone export. Retain upstream notices and obtain any additional permissions needed for your use; third-party foundations are not claimed as exclusively authored by Daniel.
