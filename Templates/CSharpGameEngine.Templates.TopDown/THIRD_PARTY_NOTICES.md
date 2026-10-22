# Third-party notices

## Unreal Engine content

`Assets/` contains content exported from Unreal Engine 5.8, from the Top Down template (`TP_TopDownBP`) and its shared Characters and LevelPrototyping content packs. This includes the SKM_Manny_Simple mannequin, its animations (shared with the Third Person template), and the level meshes and textures. The content is © Epic Games, Inc. and is licensed under the Unreal Engine End User License Agreement (https://www.unrealengine.com/eula). Its use and redistribution are subject to that agreement.

The sound effects in `Assets/Sounds/` (footstep, jump, landing) were exported from Epic's learning-course content (Northwood: GameplayDesigner footsteps and ShooterGame foley) of the Unreal Engine 5 projects of the project owner, under the same agreement.

`Assets/Cursor/SM_CursorMesh.glb` is derived from the template's `SM_CursorMesh` and its material `M_Cursor` (the cursor marker, `FX_Cursor_Success`): `tools/UnrealExport/cut_masked_mesh.py` cuts the arrow, which the material masks out of a texture, into the geometry, because the engine draws no alpha mask. It is covered by the same agreement.

The C# Game Engine project bundles the rest of this content unchanged, at its owner's decision. Anyone who uses or redistributes it is responsible for complying with the agreement.

## Engine package dependencies

A game restores these libraries through the CSharpGameEngine.Sdk package:
- SharpGLTF.Core (MIT)
- TerraFX.Interop.Windows (MIT)
- AssimpNet (MIT), including the Open Asset Import Library (BSD-3-Clause)

The engine's shader compiler carries Microsoft DirectX Shader Compiler binaries (University of Illinois/NCSA Open Source License and the Microsoft DirectX Shader Compiler license).
