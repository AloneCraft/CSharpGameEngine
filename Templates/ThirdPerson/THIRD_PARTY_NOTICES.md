# Third-party notices

## Unreal Engine content

`Assets/` contains content exported from Unreal Engine 5.0 and 5.8, from the Third Person templates and their shared Characters and LevelPrototyping content packs. The UE 5.0 level meshes use converted materials; the mannequin and animations originate from UE 5.8. The content is © Epic Games, Inc. and is licensed under the Unreal Engine End User License Agreement (https://www.unrealengine.com/eula). Its use and redistribution are subject to that agreement.

The sound effects in `Assets/Sounds/` (footstep, jump, landing) were exported from Epic's learning-course content (Northwood: GameplayDesigner footsteps and ShooterGame foley) of the Unreal Engine 5 projects of the project owner, under the same agreement.

The C# Game Engine project bundles this content, including converted materials, at its owner's decision. Anyone who uses or redistributes it is responsible for complying with the agreement.

The wall lettering mesh was generated from the UE 5.0 Third Person label using the Roboto Bold typeface included in Unreal Engine's Slate fonts.

## Engine package dependencies

A game restores these libraries through the CSharpGameEngine.Sdk package:
- SharpGLTF.Core (MIT)
- TerraFX.Interop.Windows (MIT)
- AssimpNet (MIT), including the Open Asset Import Library (BSD-3-Clause)

The engine's shader compiler carries Microsoft DirectX Shader Compiler binaries (University of Illinois/NCSA Open Source License and the Microsoft DirectX Shader Compiler license).
