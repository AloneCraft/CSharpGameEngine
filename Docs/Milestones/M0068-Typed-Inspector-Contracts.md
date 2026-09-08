# M0068 - Typed Inspector and Component Palette contracts

- Status: Completed
- Date: 2026-09-01

## Outcome

F-L8-024 is implemented as the executable headless `vsix.inspector-contracts` Stage. The Inspector uses the declared component-member type as its type source of truth. Enum values and immutable `[SceneAssets]` references share one bounded choice contract; asset candidates must belong to the referenced container, be static and immutable, and have the exact declared member type. An invalid, missing, or unchanged choice creates no `SceneEditCommitRequest`, so it cannot mutate the C# authoring source or add a native Undo entry.

The canonical numeric boundary remains explicit. `float` and integer members use validated editors; engine-owned `double` and `decimal` declarations are read-only even when their initializer is an integer literal. `InspectorEditorKind.ReadOnly` now implies `IsEditable == false` by construction.

Existing vector, text, boolean, component catalog, initializer, add, remove, reorder, drop, source-edit, and Roslyn reparse contracts are retained rather than duplicated behind a new Inspector-specific source writer.

## Verification

- Focused Inspector/ViewModel/refresh tests: 80/80 Debug.
- `vsix.inspector-contracts`: Passed in Debug and Release.
- Debug result: `.artifacts/FeatureStages/20260901T071857046Z-single-36ec58bdb2424f949b223ca20f541c01.run-result.json`.
- Release result: `.artifacts/FeatureStages/20260901T071934358Z-single-6fa797bc810545b59819eca3f437ed42.run-result.json`.
- Complete VSIX tests: 1,022/1,022 Debug and 1,022/1,022 Release.
- FeatureStageCatalog contracts: 14/14 Debug; coverage validation passed.
- Full solution tests: 2,044/2,044 Debug and 2,043/2,043 Release.
- Full solution Debug and Release builds succeeded; the only warning was the known transient retry while copying the concurrently opened VSIX test asset.

## Adversarial result

The feature does not introduce a second asset database, duplicated enum/asset UI model, Inspector-owned source of truth, or compatibility writer. Physical asset existence/import remains F-L8-019, transform visual acceptance remains F-L8-023, and cross-feature source-conflict convergence remains F-L8-025. Those boundaries prevent F-L8-024 from becoming another editor Manager or migration layer.
