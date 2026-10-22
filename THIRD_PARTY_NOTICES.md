# Third-party notices

## Unreal Engine content

`Templates/CSharpGameEngine.Templates.ThirdPerson/Assets/` (copied into every game created from the Third Person template) contains content exported from Unreal Engine 5.8, from the Third Person template (`TP_ThirdPersonBP`) and its shared Characters and LevelPrototyping content packs. This includes the SKM_Quinn_Simple mannequin, its animations, and the level meshes and textures. The content is © Epic Games, Inc. and is licensed under the Unreal Engine End User License Agreement (https://www.unrealengine.com/eula). Its use and redistribution are subject to that agreement.

The other templates (FirstPerson, TopDown, Vehicle) carry content exported the same way from the First Person, Top Down and Vehicle templates, and the footstep, jump and landing sounds of the first three come from Epic's learning-course content (Northwood) of the Unreal Engine 5 projects of the project owner; each template folder has its own `THIRD_PARTY_NOTICES.md`.

The C# Game Engine project bundles this content unchanged, at its owner's decision. Anyone who uses or redistributes it is responsible for complying with the agreement.

## Fonts

`assets/Fonts/Roboto-Regular.cgefont` and `Roboto-Light.cgefont` (shipped in the CSharpGameEngine.Sdk package and copied into every game's `Fonts/`) are the Roboto Regular and Light typefaces baked to signed distance field atlases by `tools/CSharpGameEngine.FontBaker`, from the font files that Unreal Engine 5.8 ships (`Engine/Content/Slate/Fonts`). Roboto is © Google and is licensed under the Apache License, Version 2.0; the license text is `assets/Fonts/ROBOTO_LICENSE.txt` and travels with the SDK package.

## Engine package dependencies

A game restores these libraries through the CSharpGameEngine.Sdk package:
- SharpGLTF.Core (MIT)
- TerraFX.Interop.Windows (MIT)
- AssimpNet (MIT), including the Open Asset Import Library (BSD-3-Clause)

The engine's shader compiler carries Microsoft DirectX Shader Compiler binaries (University of Illinois/NCSA Open Source License and the Microsoft DirectX Shader Compiler license).
