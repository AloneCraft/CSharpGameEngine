# ADR-0067 — Kinematic character movement, spring-arm camera and composed input
- Status: Accepted
- Date: 2026-09-30
## Context
The ThirdPerson template must be playable the way Unreal's `BP_ThirdPersonCharacter` is: WASD/left stick moves relative to the camera, the character turns toward its movement, walks up ramps and small steps, is stopped by walls, jumps, and a spring-arm camera orbits with the mouse/right stick and shortens when it hits geometry. The Unreal export already provides the Blueprint's real values (capsule 35/90 cm, MaxWalkSpeed 600, MaxAcceleration 1500, GroundFriction 8, braking 2000/1500, JumpZVelocity 500, AirControl 0.35 with a ×2 boost below 25 cm/s, rotation 500°/s, MaxStepHeight 45 cm, walkable floor 44.765°, arm 400 cm at z 8.49 cm with a 12 cm probe, FOV 90) and the Enhanced Input mappings (IMC_Default, IMC_MouseLook with Swizzle/Negate/DeadZone modifiers).
Observed engine facts: the deterministic physics world answers ray and sweep queries only against body AABBs; static bodies ignore Transform scale; no runtime path registers a mesh's triangles as a collider; capsule-versus-triangle contacts go through iterative GJK/EPA; `InputServer` keeps one value per action (several keys bound to one action overwrite each other), has no Swizzle/Negate modifiers and keeps a relative mouse delta as a held value.
## Decision
1. **One collision truth.** Level geometry collides through the existing deterministic physics world, not a character-only copy.
   - A `StaticMeshCollider` Component on a `StaticMesh` node is cooked by `StaticMeshCollisionCooker`. The cooker turns the rendered triangles into a `PhysicsShapeAsset` static triangle mesh, with the node's full world transform baked in. There is one asset per (glTF, placement), and its ID carries a hash of the placement.
   - `StaticMeshColliderSync` submits the mesh as a static body at the origin.
   - Degenerate triangles are dropped at cook time.
   - A `Collider` on the same entity is a fault.
2. **Closed-form capsule contacts.** The narrow phase gains a segment-to-triangle capsule contact beside the existing sphere-to-triangle one. Rigid-body capsules use the same code.
   - A segment that crosses a triangle is pushed out along the face normal, toward the capsule centre's side.
   - The world gains `CollectContacts`, which returns every penetrating triangle (for meshes) or body (for primitives) for a query shape.
   - Each contact also carries the triangle's own `SurfaceNormal`, facing the query shape.
3. **Kinematic, deterministic movement.** A new portable shared project `Runtime.Character` holds `CharacterMovement` (Unreal's authored values converted to metres, Y up) and its runtime state. It runs in FixedSimulation on `LogicalScalar` like the rigid-body solver:
   - Walking accelerates toward the input with MaxAcceleration and applies GroundFriction; without input it brakes with BrakingDecelerationWalking.
   - Falling integrates gravity and applies AirControl (boosted ×2 below 0.25 m/s lateral speed) and BrakingDecelerationFalling.
   - Movement is collide-and-slide. Sub-steps are no longer than half the radius. Each sub-step depenetrates from the deepest contact, up to four times.
   - While walking, a contact with a walkable face lifts the capsule straight up and leaves the velocity unchanged, so horizontal speed is kept on ramps as in Unreal. Any other contact is a wall: it gets a horizontal push, and the velocity into it is removed.
   - While falling, contacts push along their normal, and landing on a walkable face switches to Walking.
   - Floors are decided by the face normal, not the contact normal, so the capsule can stand perched on an edge. Walkable means SurfaceNormal · up ≥ cos(WalkableFloorAngle).
   - The floor search descends in radius/2 steps and then bisects. Walking snaps to a floor within MaxStepHeight.
   - A blocked walking move retries raised by MaxStepHeight. The step is accepted only when it makes more progress and its floor contact point is at most MaxStepHeight above the feet (Unreal's rule; hemisphere rounding cannot turn a 50 cm block into a step).
   - Facing is a horizontal unit vector rotated toward the input by at most RotationRate·dt, and the rotation is built with half-angle square roots. No atan2 is needed.
4. **Controller and camera are presentation.**
   - `PlayerController` on the character root holds the control yaw and pitch in `float` degrees. Pitch is clamped (default ±89°), and positive pitch looks up.
   - `PlayerControl.AddLook` and `PlayerControl.MoveDirection` are the gameplay helpers. The move direction is relative to the control yaw and becomes the `CharacterInput` of the next fixed tick.
   - `SpringArm` sits on the camera entity, which is a direct child of the character. In the Presentation phase, before `CameraOriginSync`, it rewrites that entity's local Transform so the world pose is the control rotation at the arm's end (Unreal's bUsePawnControlRotation).
   - The arm length is the longest capsule of ProbeSize, grown from the pivot backward, that overlaps nothing. It is found by bisection over `CollectContacts`. A growing capsule overlaps monotonically, so a thin wall cannot be skipped. The world is only read.
5. **Composed input (Unreal Enhanced Input semantics).**
   - Bindings keep their dead zone and scale, which run first, and gain ordered modifiers: Negate per axis, and Swizzle YXZ.
   - `InputServer.ReadValue(action)` returns the sum of every binding of the highest-priority active context that binds the action.
   - Relative controls (mouse delta, wheel) are accumulated per processed frame.
   - The existing Started/Performed/Canceled stream is unchanged.
   - Controls use Unreal key names (`SpaceBar`, `Gamepad_Left2D`, …). XInput pad 0 is polled by the Win32 layer as `InputDeviceKind.Gamepad` controls: sticks as Axis2D, triggers as Axis1D, and buttons.
   - Mouse capture clips the cursor only while the window has focus, because `ClipCursor` is system-wide.
6. **Template.** The ThirdPerson importer generates the gameplay setup from the manifest.
   - It emits `StaticMeshCollider` on every level mesh.
   - It emits `Input/ThirdPersonInput.cs` from IMC_Default and IMC_MouseLook. Stable IDs are derived from the context and action names.
   - It emits the `ThirdPersonCharacter` scene (since ADR-0070 converted to `Scenes/ThirdPersonCharacter.scene.json`) at the PlayerStart, converted to metres: the Blueprint's movement values, the PlayerController, a `FollowCamera` child with the SpringArm and Camera, and (since ADR-0066) the animated `Mesh` child that replaced the first capsule and facing-box visual.
   - Unreal's horizontal FOV of 90° becomes the vertical FOV at the template's 16:9 window.
   - Features without a port are rejected by the importer, not dropped: camera lag, socket or target offsets, FallingLateralFriction, non-radial or trailing dead zones, and swizzles other than YXZ.
   - The hand-written `Gameplay/ThirdPersonControls` system (DoMove/DoLook/DoJump) installs the input map and captures the mouse, except in automation runs. It applies mouse look at 0.175°/count, which is inferred from Unreal's 0.07 axis sensitivity × 2.5 legacy input scale and not measured in Unreal. It applies gamepad look at 150°/s, Unreal's 2.5° per frame at 60 Hz.
7. **Automation input.** `RuntimeAutomation` presses keys by presented frame (`CGE_AUTOMATION_INPUT`) through the window's input adapter. It writes each frame's camera position to a JSON report (`CGE_AUTOMATION_REPORT`). The `rigid.character-controller` Stage drives the built template this way.
## Alternatives considered
- A character-only triangle copy of the level: duplicates the collision truth and diverges from what rigid bodies collide with.
- Driving a kinematic rigid body through the solver: the solver has no step-up, floor snapping or walkable-slope logic, and its contact response is not Unreal's movement model.
- Sweep-based movement (Unreal's exact approach): needs a robust continuous capsule sweep against triangles in Q64.64; sub-stepped depenetration gives the same results at walking speeds without tunnelling (step ≤ radius/2) and reuses the contact code.
- `float` simulation: ADR-0063 allows relative float, but the rigid-body solver is `LogicalScalar`; the character uses the same numeric model so replays and snapshots stay deterministic.
## Consequences
- Movement cost per tick is small for level meshes (few triangles per capsule AABB) but grows with dense collision meshes; complex art meshes should get simpler collision assets later.
- The character is not a physics body yet: it does not push rigid bodies and rigid bodies do not push it.
- Root motion, crouch, swimming, networking/prediction and moving platforms are out of scope.
- Collision uses the rendered triangles, not Unreal's simple collision primitives, so contact shapes can differ slightly from Unreal on bevelled meshes.
- Mouse and stick look use the wall clock of the presentation frame, so camera turn speed does not depend on the fixed tick. The resulting move direction only reaches the simulation through `CharacterInput`.
- The fixed-tick scheduler retains its backlog (ADR-0055). The long first frame of level loading is therefore replayed at up to eight ticks per frame right after startup. This is observed and tracked separately, and is not decided here.
## Verification
- Deterministic unit tests:
  - capsule-triangle contacts against analytic distances;
  - falling onto a floor and settling;
  - sliding along a wall;
  - walking up a 30° ramp and failing on a 50° slope;
  - stepping onto a 40 cm step and not a 50 cm one;
  - the jump apex v²/2g;
  - turning at 500°/s;
  - identical results across repeated runs.
- Input tests for composition, modifiers, relative accumulation, gamepad events and Unreal key names.
- A mouse-capture test that an unfocused window leaves the cursor clip alone.
- Spring-arm tests:
  - the control rotation axes;
  - the pitch clamp and yaw wrap;
  - stopping a probe radius before a wall behind, without tunnelling;
  - shortening against the floor when looking up;
  - collapsing when the pivot is inside geometry.
- The `rigid.character-controller` Stage runs these tests. It then runs the built template on WARP with `W@20-40;SpaceBar@50-51`. The follow camera must move at least 1 m horizontally, rise at least 0.5 m, and come back down, without dropping through the floor and without D3D12 debug-layer errors. See [M0090](../Milestones/M0090-ThirdPerson-Character-Movement.md).
## Related features
F-L5-006 (character controller, this ADR), F-L5-003 (physics runtime), F-L4-010 (input actions), F-L0-010 (Win32 input). See [M0090](../Milestones/M0090-ThirdPerson-Character-Movement.md).
