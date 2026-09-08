# M0080 - Rendering Performance Evidence

## Outcome

F-L7-023 is implemented as one executable `rendering.performance` Stage. An isolated Windows TestHost captures exactly five warm-ups and thirty measured samples for a hidden 1280x720 D3D12 frame and for camera-relative visibility plus deterministic batching of 100,000 active instances. The Feature Runner validates the raw result and publishes one canonical SHA-bound performance report per workload.

## Source of truth and ownership

- `RenderWorldSnapshot` remains the immutable tick input; `RenderVisibility` owns stateless selection and caller-buffer batching.
- The TestHost owns the D3D12 device only for the bounded capture process. It records adapter identity, debug-layer errors, pixel verification, runtime mode, time, and current-thread allocation without placing a device or report in ECS state.
- Raw reports preserve provider observations. The common `PerformanceReportStore` owns canonical statistics, machine/Git provenance, workload metadata, and payload integrity.
- WARP is the required correctness adapter. Its elapsed frame time is recorded but is not presented as hardware performance. Release hardware is an additional pass.

## Root cause and implementation

The first Release workload measured visibility/batching p95 near 67 ms. `BuildBatches` performed heap sort for every visible set even when the completed snapshot already supplied the exact mesh/material/LOD/stable-ID order. Validation now detects canonical order while filling the caller order buffer and invokes the existing non-allocating sort only when required. The one-batch steady-state path therefore changes from O(n log n) comparison work to O(n), without changing the general ordering contract or adding state, cache, allocation, or another algorithm surface.

Initial post-warm-up outliers were then traced to Tiered JIT transitions inside the measured thirty samples. The capture process explicitly disables both .NET and COMPlus Tiered Compilation and records `tiered-compilation-disabled`; validation rejects an unrecorded/default mode. Debug records correctness, allocation, and diagnostic timing but is not a shipping timing oracle. Release visibility gates median at 6 ms, nearest-rank p95 at 8.33 ms, and allocation p95 at zero. Release hardware frame presentation gates median and p95 at 16.67 ms.

## Failure contract

Missing, duplicate, extra, incorrectly indexed, non-measurable, negative-allocation, or over-allocation samples fail closed. Wrong configuration/adapter/runtime mode/resolution, missing adapter identity, any D3D12 debug error, or failed pixel readback also fail. Each common report binds the raw file SHA-256 and independently recomputes sample statistics, budget verdict, machine fingerprint, and payload hash.

## Adversarial review

Review rejected three tempting shortcuts: accepting the 67 ms result with a loose budget, adding JIT attributes across product methods, and using WARP frame time as a hardware claim. It retained one structural fast path and one explicit measurement-process runtime setting. A multiple-batch regression uses the real StableId comparison contract so display names cannot masquerade as canonical order. Product changes introduce no `double` or `BigInteger`.

## Verification

- Rendering snapshot/visibility tests: 12/12 Debug and 12/12 Release.
- Rendering probe contract tests: 5/5 Debug and 5/5 Release within the Stage.
- Debug WARP Stage: `20260901T150011102Z-rendering-release-performance-95be44bf8480426583407c1b344028ad`.
- Release WARP Stage: `20260901T150147843Z-rendering-release-performance-b09607c003894e7691cf5fa8efbb9591`.
- Release hardware Stage: `20260901T150359915Z-rendering-performance-hardware-v2-8cc9d37684cf465d927d9a9e37d570b2`, NVIDIA GeForce RTX 4060 Ti.
- Release WARP visibility: median 4,118,550 ns; p95 4,490,200 ns; allocation p95 0; report SHA-256 `cd4b46e334f07d2f5311886a76dd491bad420dca31665d6a0979176de2a9a53c`.
- Release hardware frame: median 8,324,400 ns; p95 8,841,200 ns; allocation p95 0; report SHA-256 `25d11ef6adde0102f8848f6990c51a69aec21ec3cf0907be586c7fd9c9106d25`.
- Release hardware visibility: median 4,629,650 ns; p95 5,465,400 ns; allocation p95 0; report SHA-256 `1f722624f80c1d9df75254246cd0c76167930c1ec262d63920a96236b21738fa`.

F-L7-024 remains the single owner of the scoped Rendering release closure that consumes this evidence together with shared Preview Host and shader/artifact provenance.
