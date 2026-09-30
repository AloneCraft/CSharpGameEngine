# M0090 — ThirdPerson character movement, camera and input
- Date: 2026-09-30
- Implementation: Complete for walking/falling movement, the spring-arm follow camera, composed input with XInput, and a playable template character (capsule stand-in until ADR-0066)
- Verification: TestVerified / WARP RuntimeVerified; Stage `rigid.character-controller`
- Decision: [ADR-0067](../Decisions/ADR-0067-Kinematic-Character-Movement-And-Spring-Arm-Camera.md)
## Goal and scope
Make the ported UE5.8 ThirdPerson template playable the way `BP_ThirdPersonCharacter` is.
- The character walks relative to the camera and turns toward its movement.
- It climbs ramps and steps, slides along walls, and jumps.
- The follow camera orbits with the mouse or right stick and shortens at obstacles.

Implemented:
- Capsule-triangle contacts and StaticMesh triangle collision (`StaticMeshCollider`).
- `Runtime.Character`: `CharacterMovement`, `CharacterMotor`, `PlayerController` and `SpringArm`.
- Enhanced Input value composition, with Negate/Swizzle modifiers, relative mouse axes, XInput and Unreal key names.
- Focus-safe mouse capture.
- Scripted automation input and a camera report.
- Importer emission of the level collision, the character and the input map.

Excluded:
- The skeletal mesh and animation (ADR-0066).
- Pushing or being pushed by rigid bodies.
- Crouch, swimming, flying, moving platforms and root motion.
- Camera lag.
- Unreal's simple collision primitives.
- Measured parity of mouse sensitivity with Unreal.
- Physical gamepad, hardware-GPU certification, and interactive VSIX verification.

Changed features: F-L5-006 (now Implemented), F-L4-010, F-L0-010.
## Observed behavior and fixes found by tests
- **Ramp speed.** Walking up a 30° ramp slowed the capsule down. The cause was that walkable contacts projected the velocity onto the slope. Unreal keeps horizontal speed on ramps. Walkable contacts now lift the capsule vertically without touching the velocity.
- **Step landing.** A step-up first landed inside the block, because the floor probe searched bottom-up. The floor search now descends and then bisects, and floors are decided by the triangle's own normal, so the capsule can perch on an edge.
- **50 cm step.** A 50 cm block was climbed because the hemisphere's rounding reached over it. The step now follows Unreal's rule: the floor contact point may be at most MaxStepHeight above the feet.
- **Control rotation.** The first spring-arm tests saw the identity rotation for every control yaw.
  - The code called `Product.TryNormalize(out Product)`. `TryNormalize` cleared its `out` argument before reading the receiver. A readonly struct method takes `this` by reference, so it normalized zero and failed.
  - The same form in the physics narrow phase (`Normal.TryNormalize(out Normal)`) made every EPA run fail at its first face. The fallback then reported +U as the normal of every convex-asset and convex-vs-triangle contact.
  - A fail-first test put two convex cubes side by side and reported normal (0,1,0).
  - Both `TryNormalize` methods now read the receiver before writing the result. The test reports the separating axis with 0.1 m penetration. The complete Windows suite, including physics state hashes, was unchanged.
- **Cursor clip.** `Win32Window.SetMouseCaptured(true)` clipped the cursor immediately, and `ClipCursor` is system-wide. A hidden, unfocused test window clipped the desktop cursor to (86,109)-(206,173). Capture is now applied only while the window has focus, and `ShowCursor` calls are balanced by one flag.
- **Space key.** The Space key was reported as `VK_20`. It is now `SpaceBar`, matching Unreal's key name, so the imported IMC_Default binds it.
- **Scene DSL literals.** Scene DSL rejects `LogicalScalar.FromSingle(...)` calls (SCENE111). The generated character therefore uses float literals through the implicit conversion.
- **Startup backlog (observed, not changed here).** At startup, the level load leaves about 530 fixed ticks of retained backlog. The simulation then runs eight ticks per frame for roughly 70 frames, both on hardware and on WARP. The scripted run therefore walks 15.7 m in 20 frames. The Stage oracle only uses lower bounds, so it holds whether or not the backlog is present. Whether load time should enter the accumulator is a separate decision against ADR-0055.
## Commands and results
Commands executed from the repository root:
```text
dotnet test CSharpGameEngine.slnx -c Release
dotnet test tests/CSharpGameEngine.Windows.Tests -c Release --filter "FullyQualifiedName~SpringArmTests|FullyQualifiedName~CharacterMotorTests"
dotnet run --project tools/CSharpGameEngine.Unreal.SceneImporter --no-build -- thirdperson .artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json templates/CSharpGameEngine.Templates.ThirdPerson
dotnet build templates/CSharpGameEngine.Templates.ThirdPerson -c Debug
dotnet tools/CSharpGameEngine.FeatureRunner/bin/Debug/net10.0/CSharpGameEngine.FeatureRunner.dll verify-thirdperson-character --adapter hardware
dotnet tools/CSharpGameEngine.FeatureRunner/bin/Debug/net10.0/CSharpGameEngine.FeatureRunner.dll verify-thirdperson-character --adapter warp
dotnet run --project tools/CSharpGameEngine.FeatureRunner/CSharpGameEngine.FeatureRunner.csproj -c Debug -- run rigid.character-controller --configuration Debug --adapter warp
```
| Check | Result |
|---|---|
| Complete solution tests, Release | 2250 passed, 0 failed, 0 skipped. This includes Windows 305, VSExtension 1050, FeatureRunner 154, CodeAnalysis 146 and Core 128. |
| Importer re-run against the current manifest | 54 `StaticMeshCollider` lines plus the using and a comment were added to `Lvl_ThirdPerson.cs`. Assets and the asset catalog were byte-identical. `ThirdPersonCharacter.cs` and `Input/ThirdPersonInput.cs` were generated. |
| Template run, hardware GPU (`W@20-40;SpaceBar@50-51`, 110 frames) | walk 15.700 m, rise 1.228 m, descent 1.228 m, lowest camera height 0.985 m, 0 D3D12 errors |
| Template run, WARP (same script) | Same values as the hardware run, because both are saturated at eight ticks per frame. It took 162 s. |
| Stage `rigid.character-controller`, Debug, WARP | Passed |

The evidence images and reports are under `.artifacts/feature-evidence/rigid.character-controller/{hardware,warp}/`. The first capture shows the capsule on the central platform with the follow camera 4 m behind it. The scripted run ends with the capsule on the floor in front of the cylinder, with its shadow.
## Remaining uncertainty
- Mouse look at 0.175°/count is inferred from Unreal's default axis sensitivity and legacy input scale, not measured against Unreal.
- The gamepad path is covered by adapter tests only; no physical controller was used.
- The template has only run on this machine's hardware GPU and on WARP.
- Collision uses the rendered triangles, so it can differ from Unreal's simple collision on bevelled meshes.
