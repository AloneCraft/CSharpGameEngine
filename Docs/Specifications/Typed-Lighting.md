# Typed lighting and source authoring
- Status: Implemented; environment evidence is recorded in M0088.
- Decision: [ADR-0064](../Decisions/ADR-0064-Typed-Lights-And-Shadow-Ownership.md).
- Related features: F-L5-001, F-L8-024, F-L8-025; existing rendering recovery and glTF import capabilities.
## Ownership
C# `IScene` declarations are the authoring truth. The Inspector, SceneModel, preview process and completed render frame are derived representations. There is no mutable light kind, separate LightId, light-owned transform, renderer handle, or private conversion history.
`Runtime.Lighting` is a platform-independent shared-source project. It depends on ECS declarations and Contracts, not Windows, Roslyn or a renderer. Windows owns fixed-transform extraction and the connection to RenderServer. The shared project and its explicit source/asset list are registered in `CSharpGameEngine.slnx`.
## Components and units
| Component | Authored fields |
|---|---|
| DirectionalLight | LinearLightColor Color; Illuminance Illuminance (lux) |
| PointLight | LinearLightColor Color; LuminousFlux Flux (lm); LightRange Range (m) |
| SpotLight | Color; Flux (lm); Range (m); SpotConeAngles Cone (full degrees) |
| RectAreaLight | Color; Luminance Luminance (nit); RectangleSize Size (m); Range (m) |
| LightShadow | int Priority; ShadowBias Bias (m) |
| LightVisibility | bool Visible (default true) |
The value types live in `CSharpGameEngine.Contracts`, namespace `CSharpGameEngine.Lighting`. They are readonly structs, retain float precision, validate constructor arguments and provide no implicit numeric or inter-unit conversions. LuminousIntensity (cd) is an import/conversion quantity, not a common component intensity.
Flux, illuminance, luminance, intensity, bias and Linear RGB must be finite and nonnegative. Zero emission is legal. Range and each rectangle dimension must be finite and positive. Cone updates are atomic and require `0 < inner <= outer <= 180`. Default range, size and cone values are invalid and are checked at Scene analysis and renderer submission boundaries.
The same Entity owns one analytic light and a Transform. Multiple analytic lights or an orphan LightShadow are diagnosed. Multiple emitters use child Nodes. Transform owns position and orientation; Transform Scale does not multiply authored rectangle dimensions. LightShadow presence requests shadows; absence requests none. LightVisibility (ADR-0078/M0109, UE's SetVisibility) hides the node's light when `Visible` is false: the light is still validated but is not submitted, so it neither lights nor casts shadows; absence means visible; on a node without a light it does nothing (not diagnosed, unlike an orphan LightShadow). It is a companion component, like LightShadow, so the light types keep only physical quantities (light conversion rebuilds every light field as one). The backend alone chooses shadow techniques, budgets and atlas allocation.
Default lights retain white emission, directional 100000 lux, point/spot 800 lm, range 10 m, spot 30/45 degrees, rectangle 100 nit at 1 x 1 m. Default shadows use priority 0 and bias 0.005 m.
## Rendering and recovery
RenderServer accepts DirectionalLightSubmission, PointLightSubmission, SpotLightSubmission and RectAreaLightSubmission. Each has its applicable quantities and pose data, plus nullable LightShadowSettings. There are no public Kind/Intensity/ShapeA/ShapeB payloads at this boundary.
RenderFrameState has reusable typed buffers, a separate emissive record, and ordered (kind,index) metadata. SetDirectional commands also remain in order: replay must reproduce replacement semantics, including commands before a replacement. Completed and pending frames use the existing two-buffer ownership; replay never modifies the completed frame.
Invalid runtime components skip their own submission. The runtime companion records the offending property on the same Entity; other lights continue. Submission validation also rejects invalid default shapes, nonfinite positions and degenerate directions. Backend ArgumentException failures are contained per light. The native GPU layout/tags, attenuation, smooth-cone photometry, LTC, shadow algorithms and Shader ABI remain unchanged.
No per-light object or interface boxing is introduced in extraction or typed recording. Warm-buffer recording and mixed-light WARP recovery have explicit tests.
## Imports
ImportedLight is an abstract color-only base with ImportedDirectionalLight, ImportedPointLight and ImportedSpotLight variants. The format kind is dispatched once in GltfImporter. Directional intensity is read as lux; local intensity is read as cd. LightPhotometry performs explicit point/spot cd-lm conversions matching the existing squared-smooth cone distribution.
Optional glTF range remains null when omitted. Source half-radian spot angles are retained without loss. glTF permits a zero inner angle while authored SpotConeAngles deliberately does not: ImportedSpotCone.ToAuthoredCone rejects that case instead of silently clamping or changing flux. Callers must resolve that authored input explicitly. Unreal's existing directional Scene emitter generates typed lux/color and a separate LightShadow.
## Static editing
LightValueContract is the single authoring whitelist for units, constructor parameter names/order, parsing, validation and formatting. Roslyn symbols identify the declared type and exact float constructor. Named arguments and target-typed new expressions work. User code and constructors are never run in the editor.
Each physical value is one editable row: numeric quantity, RGB triple, angle pair or rectangle pair. Constant expressions and static references retain their read-only semantics. Literal field initializers provide the palette defaults; invalid or unchanged input produces no source/Undo operation.
Range, cone and rectangle handles edit the corresponding whole value. A rectangle size edit updates width and height together; a cone edit updates both angles together. Direction is edited through Transform, not a hidden light direction property.
The palette adds the light, any missing Transform and LightShadow in a single source transaction. Removing a light also removes its owned shadow request in that transaction, leaving unrelated components intact.
## Explicit edit-time conversion
Four types support 12 directed conversions; selecting the current type makes no change. Runtime component replacement is not implemented. Conversion UI is disabled while the game process is running; preview uses the ordinary source rebuild path.
Color is preserved. Point/Spot preserve flux and range. Point/Spot/Rect preserve total flux using the one-sided Lambertian relation `flux = pi * luminance * width * height`. New rectangle size is 1 x 1 m; new spot cone is 30/45 degrees; newly required range is 10 m.
Conversions involving Directional require explicit input of the target-dimensional quantity. Defaults are suggested, never copied as if lux, lm and nit were interchangeable. Unevaluable required expressions also require explicit input. The UI shows values and the properties that will be discarded.
Node identity, name, Transform, unrelated components and LightShadow are retained. The old component and its settings survive only in native source Undo, not hidden fields. A conversion checks the latest source expression and source/target default-contract fingerprint, recompiles/reanalyzes the result, and yields one native text write. Conflict, invalid input and cancellation write nothing.
## Verification scope
See [M0088](../Milestones/M0088-Typed-Lighting.md) and the automated tests named there. WARP pixel equality is real rendering evidence. Native Undo routing is verified by transaction/architecture tests; those tests are not a claim of an interactive Visual Studio UI session.
