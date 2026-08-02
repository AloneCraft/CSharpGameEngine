# Test Fixture Attribution

These glTF sample files are used as automated test fixtures for
`CSharpGameEngine.Import.Gltf.Tests`. They are third-party sample assets from the official
[Khronos glTF-Sample-Assets](https://github.com/KhronosGroup/glTF-Sample-Assets) repository,
not original engine content.

- `Box.glb` — © 2017 Cesium. License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
  Source: `Models/Box/glTF-Binary/Box.glb`.
- `Triangle.gltf` / `Triangle.bin` — License: [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)
  (public domain). Source: `Models/Triangle/glTF/`.
- `BoxTextured.glb` — © 2017 Cesium. License: CC-BY 4.0 International with Trademark Limitations
  (SPDX `LicenseRef-CC-BY-TM`), plus a separate Cesium Trademark/Logo restriction (SPDX
  `LicenseRef-LegalMark-Cesium`) on the embedded Cesium logo image specifically. Source:
  `Models/BoxTextured/glTF-Binary/BoxTextured.glb`.
- `Malformed.glb` — hand-authored garbage bytes (not a real glTF/GLB file), used only to test
  that a corrupt file produces a failed import result rather than an unhandled exception.
