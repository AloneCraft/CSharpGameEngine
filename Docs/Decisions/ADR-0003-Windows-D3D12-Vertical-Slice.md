# ADR-0003: Windows Host + Direct3D 12 Vertical Slice

## Status
Accepted

## Context
Milestone 2 needs the first real Windows execution path: a raw Win32 window, a Direct3D 12
device, a swap chain, DXC-compiled HLSL shaders, and one colored triangle drawn every frame.
This is a vertical slice, not the final engine architecture - it exists to prove the path
`Windows process → Win32 window → D3D12 device → command queue → swap chain → DXC → root
signature → PSO → vertex buffer → draw → present → fence sync → clean exit` actually works end
to end from C#, without Visual Studio, before any higher-level rendering abstraction is designed.

[[ADR-0001-Layered-Architecture]] already reserves L0 for "Platform / Native Backends" and
requires that runtime layers (L0-L6) stay independent of the Visual Studio/Roslyn toolchain.
This ADR is about what goes *inside* L0 for Windows/D3D12, and about not letting that L0 code
leak into `CSharpGameEngine.Core`.

## Decision

### 1. Windows-specific code is isolated at L0, `Core` stays platform-neutral
Three new L0 projects hold all Windows/D3D12/DXC code:
- `src/CSharpGameEngine.Platform.Windows` - raw Win32 window + message loop.
- `src/CSharpGameEngine.Compiler.Hlsl` - DXC-based HLSL → DXIL compilation.
- `src/CSharpGameEngine.Graphics.D3D12` - device/swap chain/pipeline/frame loop.

`CSharpGameEngine.Core` gains no reference to any of these and stays on plain `net10.0` (no
`-windows` suffix, no Win32/D3D12/DXC package references). `CSharpGameEngine.TestHost` is the
only executable that references the Windows-specific projects and drives them; this matches the
existing "CLI/Compiler/Bake flows must run without Visual Studio" rule from ADR-0001 while
keeping the Windows dependency out of the reusable engine kernel.

`Graphics.D3D12` does not reference `Platform.Windows`: `TriangleRenderer.Create` takes a raw
`nint` window handle, not a `Win32Window`. The two L0 subsystems stay siblings composed only by
`TestHost`, so a future non-Win32 window source (or a future non-D3D12 backend) does not require
touching the other side.

### 2. No WinForms / WPF / WinUI / MAUI
The task is to prove a **raw** Win32 → D3D12 path, not to adopt a UI framework. All three
frameworks pull in large managed UI stacks, hidden message-pump behavior, and DPI/composition
layers that would obscure exactly the mechanics this milestone needs to get right (window class
registration, `WndProc` dispatch, `WM_SIZE`/`WM_CLOSE`/`WM_DESTROY`, swap chain resize). A
future authoring UI (if any) is free to pick a framework later; the runtime window used by the
engine and its `TestHost` does not need one.

### 3. Native binding: TerraFX.Interop.Windows (+ Microsoft.Direct3D.DXC for the DXC binaries)
Requirement was to pick **one** low-level binding for Win32/DXGI/D3D12/DXC, blittable-first, no
C++ relay DLL, and no bespoke whole-SDK generator at this stage. Options considered:

| Option | Verdict |
|---|---|
| **TerraFX.Interop.Windows** | Chosen. Pre-generated (ClangSharp) 1:1 projection of Win32, DXGI, D3D12, and `dxcapi.h`. COM interfaces are `unmanaged` structs with a raw `void** lpVtbl` and generated methods that do a direct `delegate* unmanaged` vtable call - no per-call managed wrapper object, no marshaling, no boxing. Ships as a single package (`TerraFX.Interop.Windows`) targeting `net10.0` directly, MIT licensed, actively maintained (Windows 11 26100 + Agility SDK 1.618 as of the version used here). |
| Vortice.Windows | Rejected. Also low-level and widely used in .NET game dev, but its COM layer wraps every interface in a managed `ComObject`-style class with reference counting/finalizers - exactly the "high-level wrapper that mints lots of managed objects" this milestone was told to avoid. |
| Silk.NET | Rejected for this milestone. Broad and actively maintained, but its D3D12/DXGI surface goes through a generated interface+`ComPtr` abstraction layer with more indirection than a raw vtable call; heavier than needed for one triangle. |
| Hand-rolled P/Invoke over the whole Windows/D3D12/DXC surface | Rejected. Explicitly out of scope for this stage ("Windows SDK全体の独自Binding Generatorは作らない"); reinvents what TerraFX already ships, and blittable-struct-per-API hand authoring for D3D12 alone is hundreds of types. |
| C++/CLI or a native relay DLL | Rejected outright per the task constraints. Adds a second toolchain (a C++ compiler) to a "no Visual Studio required" build, and defeats the purpose of proving a pure-C# path. |

`TerraFX.Interop.Windows` is referenced directly by `Platform.Windows`, `Compiler.Hlsl`, and
`Graphics.D3D12`. No other project references it, and none of its `TerraFX.Interop.*` types
appear in a public API outside those three L0 projects (see "No binding leakage" below).

DXC itself (`dxcompiler.dll` / `dxil.dll`) is not part of the Windows SDK and the copy bundled
with older Windows SDKs does not reliably support current Shader Model 6.x. The redistributable
is pulled from the official `Microsoft.Direct3D.DXC` NuGet package. That package ships
`build/native/*` assets meant for vcxproj consumers only (the .NET SDK's project system does not
auto-import a `build/native/` folder), so `CSharpGameEngine.Compiler.Hlsl.csproj` references it
with `GeneratePathProperty="true"` and manually copies the x64 `dxcompiler.dll`/`dxil.dll` next
to any executable that (transitively) depends on the project. This is a one-time MSBuild
`<None Include=... CopyToOutputDirectory="PreserveNewest">` block; ARM64 is out of scope for this
milestone (noted as a known gap - see "Unresolved" in the completion report).

### 4. HLSL lives as a real `.hlsl` source file, compiled by DXC
`assets/Shaders/Triangle.hlsl` is authored as a normal HLSL file (not a C# string literal) with
`VSMain`/`PSMain` entry points, Shader Model 6.0 profiles (`vs_6_0`/`ps_6_0`), a `POSITION`/
`COLOR` vertex input, and `COLOR` interpolated into the pixel shader. `HlslCompiler.Compile`
wraps `IDxcUtils`/`IDxcCompiler3` (`DxcCreateInstance` against `CLSID_DxcUtils`/
`CLSID_DxcCompiler`): it takes a file path, entry point, target profile, and an optional define
list; on success it returns non-empty DXIL bytecode, and on any failure - bad entry point,
syntax error, DXC reporting zero-length output - it returns an explicit failure with parsed
`file(line,col): severity: message` diagnostics. Debug builds request `-Zi -Od -Qembed_debug`
(embedded debug info, unoptimized); Release requests `-O3`. Include graphs, a shader cache,
variants, hot reload, and reflection are explicitly out of scope for this milestone.

### 5. No render graph, no general Graphics API yet
`CSharpGameEngine.Graphics.D3D12` exposes exactly what this slice needs: `GraphicsDevice`
(factory/adapter/device/queue/debug layer), `TriangleRenderer` (swap chain, RTV heap, root
signature, PSO, vertex buffer, per-frame draw, resize, GPU readback), and small pure-logic types
(`TriangleRunOptions`, `TrianglePixelVerifier`) kept separate specifically so they are unit
testable without a GPU. There is no `IRenderPass`/`IRenderGraph`/generic resource-binding
abstraction, no bindless resources, no depth buffer, no MSAA, no material system. Building that
generalized surface now, from a single triangle, would be designing it blind; the next milestone
that actually needs multiple draw calls or resources is the right place to discover its shape.

### 6. Vertex buffer uses an upload heap
The triangle's vertex buffer is created directly in a `D3D12_HEAP_TYPE_UPLOAD` heap and
written via `Map`/`CopyBlock`/`Unmap` once at startup (`TriangleRenderer.CreateVertexBuffer`).
This is the same tradeoff Microsoft's own `D3D12HelloTriangle` sample makes and calls out
explicitly: upload heaps are not the recommended long-term home for static geometry (every GPU
read effectively goes through PCIe-visible memory), but for three vertices created once and never
updated, the default-heap + copy-command + fence-wait machinery would be pure ceremony. A default
heap + explicit upload path is a fair next-milestone concern once real meshes exist.

### 7. Culling
The task's example vertex order (top, then bottom-left, then bottom-right) is counter-clockwise
in NDC space, which is back-facing under D3D12's default rasterizer state
(`FrontCounterClockwise = FALSE`, `CullMode = BACK`) and would render nothing. Rather than
silently reordering the vertices away from the example in the task, the pipeline state disables
culling (`D3D12_CULL_MODE_NONE`) for this single triangle. Real culling behavior is a concern for
whenever this engine has actual closed meshes.

### 8. Frame synchronization: two frame contexts, not "wait every frame"
`TriangleRenderer` keeps two command allocators and one reused command list/fence, and follows
the same `MoveToNextFrame` pattern as Microsoft's `D3D12HelloFrameBuffering` sample: signal the
value the *current* slot was waiting on, advance to the next back buffer, wait only if that
slot's own last recorded value has not yet completed, then bump it. A full `WaitForGpuIdle`
(signal + wait + drain) is reserved for startup-failure cleanup, resize, and shutdown - not the
steady-state per-frame path.

One real bug surfaced and was fixed while building this: `IDXGISwapChain::ResizeBuffers` resets
DXGI's back-buffer index cycle (typically back to 0), which desynchronizes the two per-slot fence
values from the fence's actual completed value and can make a later `SetEventOnCompletion` wait
on a value nothing will ever signal (an infinite hang, caught by the `TriangleGpuIntegrationTests`
resize case, not a validation-layer message). The fix mirrors what `DXSample.cs`'s
`CreateWindowSizeDependentResources` does in the reference samples: after confirming the GPU is
idle, every slot's fence value is resynchronized to the just-confirmed value *before*
`ResizeBuffers` runs.

### 9. Native binding types do not leak into game-facing APIs
`TerraFX.Interop.*` types are used inside `Platform.Windows`, `Compiler.Hlsl`, and
`Graphics.D3D12` only. The public surface those three projects expose to `TestHost` uses plain
.NET types and this engine's own types (`nint` window handles, `byte[]` bytecode,
`HlslDiagnostic`, `PixelColor`, `TriangleRunOptions`, etc.) - the one narrow exception is
`DXGI_FORMAT` on `PixelReadbackResult`/`TrianglePixelVerifier`, which stays inside
`Graphics.D3D12` itself (an L0 project) and is never referenced from `Core` or any future
gameplay-facing layer.

### 10. Future NativeAOT / raw-binding boundary
Because the Windows/D3D12/DXC surface is fully isolated behind `Platform.Windows`,
`Compiler.Hlsl`, and `Graphics.D3D12`, a future milestone can evaluate NativeAOT publishing or a
different low-level binding (or TerraFX's own trimming/NativeAOT support) by changing those three
projects without touching `Core` or the shape of `TestHost`'s CLI.

## Consequences
- `Core` remains buildable and testable on any OS/CI agent; only `Platform.Windows`,
  `Compiler.Hlsl`, `Graphics.D3D12`, `TestHost`, and their test projects require Windows.
- Adding a second graphics backend later (or swapping the window layer) does not require
  touching `Core`, and does not require `Graphics.D3D12` and `Platform.Windows` to know about
  each other - only the composition root (`TestHost`, and later a real host) does.
- The vertex-buffer-in-upload-heap and no-render-graph choices are explicitly temporary; revisit
  once a milestone needs more than one static mesh or more than one draw call.
- ARM64 DXC binaries are not wired up; `Compiler.Hlsl.csproj` only copies the x64
  `Microsoft.Direct3D.DXC` redistributable. Tracked as a known gap, not silently ignored.

## ADR-0034 amendment: where the DXC native copy now lives

[[ADR-0034-Project-Consolidation]] merged `CSharpGameEngine.Compiler.Hlsl`,
`CSharpGameEngine.Platform.Windows` and `CSharpGameEngine.Graphics.D3D12` into a single
`CSharpGameEngine.Windows` assembly. **Every namespace was kept unchanged**, so
`CSharpGameEngine.Compiler.Hlsl.HlslCompiler` and friends are still spelled exactly the same in
source; only the assembly they live in changed.

The `<None>` block that copies `dxcompiler.dll` / `dxil.dll` out of
`$(PkgMicrosoft_Direct3D_DXC)\build\native\bin\x64\` moved **verbatim** from
`src/CSharpGameEngine.Compiler.Hlsl/CSharpGameEngine.Compiler.Hlsl.csproj` to
`src/CSharpGameEngine.Windows/CSharpGameEngine.Windows.csproj`. The reasoning in this ADR is
unchanged: `Microsoft.Direct3D.DXC` ships `build/native/*.targets` meant for vcxproj consumers
only, so SDK-style C# projects must copy the redistributable themselves.

Because losing that block breaks `HlslCompiler` **at runtime only** - no unit test would catch it -
the move was verified by inspecting the real output directories and running the renderer:

```
tools/CSharpGameEngine.TestHost/bin/Debug/net10.0-windows10.0.19041.0/{dxcompiler.dll,dxil.dll}      present
tools/CSharpGameEngine.Extension/bin/Debug/net10.0-windows10.0.19041.0/win-x64/{dxcompiler,dxil}.dll present
UserGameProject/bin/Debug/net10.0-windows10.0.19041.0/win-x64/{dxcompiler,dxil}.dll                  present

TestHost.exe triangle --hidden --frames 3 --warp --debug-layer --verify
  → Shader compilation: OK (0 diagnostics) / Verification: PASSED / D3D12 validation errors: 0
```

Note on how these executables must be launched: running them as `dotnet <app>.dll` (through the
muxer) fails to load `dxcompiler`, because the app directory is not added to the native DLL search
path in that mode. Launching the generated apphost `.exe` works. This behaviour predates ADR-0034 -
it was reproduced identically on the pre-consolidation commit `f17fbd5` in a separate worktree.
