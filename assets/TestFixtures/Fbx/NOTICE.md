# Test Fixture Attribution

This FBX sample file is used as an automated test fixture for
`CSharpGameEngine.Import.Fbx` and as a CLI/VSIX sample. It is a third-party sample asset from the
official [Assimp](https://github.com/assimp/assimp) repository, not original engine content.

- `Box.fbx` — from `test/models/FBX/phong_cube.fbx` in the Assimp repository (renamed locally for
  consistency with the glTF fixtures). License:
  [BSD 3-Clause](https://github.com/assimp/assimp/blob/master/LICENSE) (the Assimp repository's
  default license; this file is not under the `test/models-nonbsd` exception).
  Note: `test/models/FBX/box.fbx` (the more obviously-named file) was tried first but fails to
  import under AssimpNet 4.1.0's bundled native Assimp version - its vertex-color layer uses a
  `ColorIndex` element the FBX parser in that version doesn't recognize
  ("FBX-Parser did not find required element \"ColorIndex\""), so the scene comes back with zero
  meshes. `phong_cube.fbx` imports and renders correctly and was used instead.
