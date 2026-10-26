# M0123 — Solution Explorer linked asset workspace

- Date: 2026-10-26
- Status: Implementing
- Decision: [ADR-0081](../Decisions/ADR-0081-Asset-Workspace.md), accepted user implementation plan
- Feature: F-L8-035 (PartiallyImplemented)
- Specification: [Asset Workspace](../Specifications/Asset-Workspace.md)

## Asset icon plan implementation

The approved icon plan is implemented with the existing VS catalog/CrispImage path. AssetWorkspaceIcons is the single L8 mapping used by rows and Inspector. The window tab and all three localized VSCT command/filter entries use ImageGroup. Kind icons, 32 DIP thumbnail fallback, 16 DIP markers/details/Inspector, independent 12 DIP lock/error/warning badges and themed thumbnail overlay plates are present. Data/Contracts/persistent metadata are unchanged. Import-running/reimport-needed signals remain unavailable with M0114; no progress state is invented.

AssetWorkspaceIconTests covers all eight kinds, unknown-kind fallback, normal-state badge suppression, warning/error mapping, and read-only/error coexistence. An additional case reproduced a ReadOnly projection hiding its retained metadata diagnostic; the shared presentation now shows the error and the lock. The first Release snapshot failed that added case; rebuilt source passes all 1,232 tests in Debug and Release. After the final overlay-plate XAML, the focused Debug icon/localization/packaging selection passes 11/11. Logs: `.artifacts/asset-icons-final-test-debug.log`, `asset-icons-verified-test-release.log`, `asset-icons-verified-focused-debug.log`. Theme-fix builds/tests are recorded after the actual verification below.

Dedicated CgeAssets installation `AssetWorkspace/install-icons.log` verified main assembly SHA-256 67E0A0C4AE5A5B42B25B64893ADAED32EA04F97D8B5E98CFB92921702049F5A5. Actual Japanese/dark and light shell observations show model/scene/font/texture icons, normal PNG thumbnails with type markers, invalid-PNG fallback, lock+metadata-error coexistence in tiles/details/Inspector, selected rows and sorting of the templated State column. Owned smoke fixtures are tracked by exact paths/hashes in `.artifacts/asset-icons-fixtures.json` and removed after verification. Light inactive selection exposed white text on a pale background; AssetTheme now uses WindowText for an inactive selected item and HighlightText for an active selected item. This correction requires the final shell pass recorded below.

High-contrast mode, an explicit 100/150/200 percent monitor-DPI matrix, and the complete disabled-command matrix have not been exercised. Those checks remain acceptance gates; automated mapping tests and normal-theme screenshots do not establish them. This icon work does not complete the broader asset-workspace feature.

Final theme-fix builds pass with zero warnings/errors in Debug and Release (`asset-icons-theme-build-*.log`). Final Release VSIX tests pass 1,232/1,232 (`asset-icons-theme-test-release.log`); the final Debug icon/localization/packaging subset passes 11/11 (`asset-icons-theme-focused-debug.log`), following the earlier full 1,232/1,232 Debug run. `AssetWorkspace/install-icons-theme.log` verifies the final installed Debug main assembly BEE75D3773A783E599DE7146592172B91D19F7C91BE258E01A18E208B5519F13. These focused icon changes introduce no projects or public/persistent APIs; the full solution is not rerun for this presentation-only increment.

The first inactive-foreground trigger alone did not correct the light selected tile. The [WPF Aero item template](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Themes/XAML/ListBoxItem.xaml) paints its selected Border with template-owned static colors; replacing system selection brush resources is insufficient for that template. Assets tiles therefore use a lightweight Border/ContentPresenter template bound to the existing VS-themed item style. Active selection uses the matching Highlight/HighlightText pair; inactive selection uses Window/WindowText with a selection border, and disabled items use GrayText. This is local to the Assets control; it does not modify the VS shell visual tree or Windows display settings. Container-fix builds/tests and the final actual observation supersede the incomplete first theme correction.

Final container builds pass with zero warnings/errors in both configurations (`asset-icons-container-build-*.log`). Release passes 1,232/1,232 (`asset-icons-container-test-release.log`); final Debug focused icon/packaging tests pass 11/11 (`asset-icons-container-focused-debug.log`). The installed and actually exercised final Debug main assembly is 5FB5685A43618A21C6151A232E7618EA8CCFA7C1E15F8D81829C7D9E1022A106 (`AssetWorkspace/install-icons-container.log`); Release main assembly is 599521C39CCE9747A289CA4012633AC7663EA7420BFC91E719466382BFBB599C. In the final actual light-theme pass, a selected tile rendered white text on the VS blue highlight; focusing the separate Component palette textbox changed it to black text on the normal background with the selection border retained. Both states are readable, and the lock/error markers remain visible. This is actual shell evidence, not an inference from the policy tests. The original dark setting is restored after the pass.

Current icon package SHA-256: Debug e79e28768bdf773eb69fbe2a1c410b966e368a3e4bc22a8b5fd80abae1be3740; Release 9c85a475dcb8138a1f40e2154a3e9cd7d4863f1d74a3cc6a862057d0d97108b4. These supersede the workspace-baseline hashes below. Final dark-theme observation also retains readable inactive selection text and the independent badges.

Cleanup verified all three owned smoke files against their creation SHA-256 values, then removed exactly those files after closing the clean validation instance. No smoke document was opened or saved. The CgeAssets theme was restored to dark; user source assets and other running applications were not changed by the icon verification.

## Implemented scope

| Plan area | Source implementation | Verification boundary |
|---|---|---|
| Design/ledger | ADR-0081, Asset-Workspace, architecture, ledger, reverse specification, conformance, Roadmap, `vsix.asset-workspace` | Catalog/ledger tests pass; this milestone retains outstanding acceptance |
| Metadata | Removed AssetMetaWatcher; explicit Content GUID-line conversion preserves GUID/hash; inventory canonical JSON only | Content tests; browsing has no writes or ID allocation |
| Exploration | Public hierarchy filter, evaluated project/plugin source inventory, location following/locking, project/kind/provider/state search, favorites, virtualized tiles/details, visible PNG/JPEG thumbnails, watcher refresh | Standalone 10,000-item cancellation and query tests; actual Japanese/dark UI displays 36 game-project/provider assets |
| Selection/edit | Existing scene/graph/model document open, asset Inspector, texture/audio information/preview, shared typed DataAsset scalar/enum/nested/array editing, native buffers/save/undo, typed-reference reveal/drop, model hierarchy placement | Automated DataAsset/reference tests; shell gesture matrix incomplete |
| Organize | Before/after full-text review; Roslyn symbols including aliases/static imports/catalog containers; parsed JSON/graph paths; source/metadata/read/compilation guards; native linked Undo and physical primitive; ID retention/new ID; recoverable delete/case-only rename/pending physical-operation recovery | Content and net472 tests; native multi-document Undo and standard SE interception require end-to-end confirmation |
| Distribution | VSIX 0.1.8 builds and packages existing standalone host; deployed to a separate CgeAssets experimental hive | Deployment script verifies assembly SHA-256 and resources; M0114 SDK import distribution gate remains |

No importer/renderer was added to the VSIX. Existing physics/destruction/rendering changes in this shared checkout were preserved.

## Tests and reproducible commands

Run with normal process/file-replacement permissions; the restricted sandbox cannot perform all Windows replace/move/process tests.

```powershell
dotnet test tests/CSharpGameEngine.Content.Tests/CSharpGameEngine.Content.Tests.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Content.Tests/CSharpGameEngine.Content.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.FeatureRunner.Tests/CSharpGameEngine.FeatureRunner.Tests.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet test CSharpGameEngine.slnx -c Debug -m:1 -p:UseSharedCompilation=false
```

The full Debug run completed with 23 suite results: **3,787 passed, 1 failed, 2 skipped** (exit 1). Content 65/65, generator 338/338, bridge 36/36, VSIX 1,227/1,227 and FeatureRunner 236/236 passed. Windows passed 908 executable cases with two optional radial-evidence skips; its runtime was 8 minutes 1 second. Log: `.artifacts/asset-workspace-solution-debug-current.log`. This solution snapshot preceded the final MissingSource UI guard and wording changes; final-source VSIX verification is recorded separately. Logs are local artifacts, not committed release evidence. The shared checkout's other tasks can change suite counts.

The final L8 Debug build passed with zero warnings/errors (`asset-workspace-final-debug-build.log`), followed by VSIX **1,227/1,227** (`asset-workspace-final-debug-vsix.log`). After the final Content Undo-identity guard, both complete dependency closures were rebuilt and packaged with zero warnings/errors (`asset-workspace-final-pack-debug.log`, `asset-workspace-final-pack-release.log`); Content **66/66** and host bridge **36/36** pass in both configurations (`asset-workspace-content-final-*.log`, `asset-workspace-bridge-final-*.log`). Every packaged Content DLL was compared with its current configuration/platform build. Different platform builds must not be mistaken for stale payloads merely because their hashes differ.

| Workspace baseline VSIX 0.1.8 (before icons) | Package SHA-256 | Main assembly SHA-256 |
|---|---|---|
| Debug | 733f02aa5b6048fafda55245b30023d62730866b29fab2ee152bae5247b94a77 | 181a2b37a9c44b8844cdd6151d7da477e2c1abce0c3e1c517516fa2c6b007e2f |
| Release | 238787e4f7c2bbf137221eb2ceb1f71cf6808102ee88a9c4834ad1a10eebfd3c | 2210f9437574af9d224a4263daf15b80a607a64db2d61d5436f0eec7f08c87fe |

Packages are under `tools/CSharpGameEngine.VSExtension/bin/{Debug,Release}/net472/`. The last CgeAssets installation (`AssetWorkspace/install-current.log`) is the earlier FE12BECF101912A91F2E490A9098689011FCB1F9BA96FB4926D99FB6836DA4DD assembly snapshot. Additional computer-use requests reported an active request, so final-package installation and further native file-operation gestures were not completed. The dedicated instance was reopened with no owned smoke source or dirty smoke document; do not treat its startup log as final-package UI verification.

ShaderGraph.Cli's ProductionStage_ExternalSubgraphsPreserveGoldenHlsl still fails: expected hash prefix 31CEC528, actual 952B9B70 (line 117). The Stage graph has unowned rendering changes; this task does not alter its golden oracle. Earlier whole-solution attempts also stopped on unowned physics-test accessibility errors; the current run compiles that suite and exercises it. Do not infer whole-solution success from focused results.

The full Release run completed with 23 suite results: **3,785 passed, 1 failed, 2 skipped** (exit 1). Its only failure is the same ShaderGraph golden assertion. Windows passed 906 executable cases with two optional radial-evidence skips in 3 minutes 27 seconds. VSIX 1,227/1,227, FeatureRunner 236/236 and generator 338/338 passed. Log: `.artifacts/asset-workspace-solution-release-current.log`. This solution snapshot contains the final L8 MissingSource guard and wording, and precedes the Content Undo-identity guard, which has its own final Debug/Release suite and rebuilt package checks above.

## Experimental Instance evidence

The dedicated `/RootSuffix CgeAssets /LCID 1041` instance opens `UserEcsProject.slnx`; default and CgeFixed hives are not reconfigured. First-run setup was completed through the actual Windows UI. Japanese labels, 34 inventory items, old-metadata diagnostics, details columns and menu entries were observed. A white default WPF list and low-contrast checkbox text in dark mode were reproduced and changed to VS standard resources/styles. A subsequent capture showed readable themed controls. Restart retained the bottom Assets window. Opening Output showed Assets and Output in the same shell tab-group identity.

On the target VS instance the attempted IVsWindowFrame geometry path returned E_NOTIMPL during explicit layout. A subsequent EnvDTE linked-frame Height assignment resized the main IDE window instead of the bottom tab group. Both geometry approaches have been removed from the new asset placement path. Default tab registration uses [ProvideToolWindow](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-a-tool-window?view=visualstudio), with DockedHeight=280 as a default hint; an existing Output group retains its saved height (192 DIP in this instance). Explicit asset layout joins Output through the public linked-window API without assigning geometry. Applying approximately 280 DIP to an existing group remains unimplemented. Keyboard Undo/Save additionally enters through the pane's PreProcessMessage boundary because VS may consume global shortcuts before WPF KeyDown; no package-global standard-command override is registered.

## Remaining acceptance gates (no approved scope reduction)

- Connect M0114's canonical import/reimport service, progress/cancellation/error-list diagnostics, immutable artifacts, importer settings, subasset catalog/identities and model thumbnails; validate complete SDK/VSIX distribution. M0126 now registers/cooks dropped PNG/JPEG and FBX files and enables their Inspector reimport/settings; complete M0114 integration, progress and distribution gates remain.
- Complete non-WPF image-format previews. Baked-font atlas previews now reuse the canonical FontAtlas codec through the standalone host; TTF/OTF use WPF font presentation. Existing model/editor open remains available; this does not establish new catalog thumbnails or importer settings.
- Implement project XML item refactoring and directory-level/batch organization. Current exact Include/Update/Remove matches, directories containing assets and editable sources shared across loaded projects stop before writing.
- Provide a dependency-aware bundle plan for relative glTF buffers/images. Current relocation is rejected if the same bytes are absent at the destination.
- Finish viewport drop placement and broader dependency/referencer presentation; current placement targets the scene hierarchy and reference queries list parsed referencers.
- Provide clickable breadcrumb navigation; the current location is path text. DataAsset rows currently cover serialized members, including nested/array spans; omitted constructor/default members are not fully surfaced and require JSON editing.
- Exercise first-show focus/280 DIP, closed/floating/restored panes, explicit layout, Source Explorer filter/selection/rename/drop, multi-project/provider cases, native linked Undo/Redo/save/external updates/interruption, English/light/high-DPI and keyboard-only use in a real VS instance. Passing standalone inverse tests do not satisfy these UI gates.
- Complete a clean final Debug/Release solution gate. No new projects were introduced; existing solution-registration checks remain part of the suite.

The accepted completion criterion remains finding, comparing, editing and safely organizing assets end to end. This milestone is not complete while these gates remain.

## Actual VS defects found and corrected

- A plain object is rejected by the real VsUndoHistoryRegistry with NotSupportedException before source creation. The file-operation context now implements the SDK IPropertyOwner contract; the registry still owns all undo history. There is no custom history implementation. The original failed attempt created no test source.
- Modal asset dialogs lacked an owner and could open partially off-screen; they now use the VS shell owner and CenterOwner, with VS theme resources.
- Imported/SDK font items were excluded by physical-folder scoping. Query scope now includes their evaluated logical Link path, while retaining the provider read-only guard. The actual game-project view now contains 36 items including two Roboto font atlases.
- Shader graph relocation now rebases parsed dependencies to the new document directory while preserving their identity; a regression test exercises this behavior.
- The first DataAsset edit opened a buffer before a text view had necessarily initialized its undo manager. OpenBuffer now obtains the SDK ITextBufferUndoManager before any write; the manager owns the buffer change primitives. With build SHA-256 AC43D373D35174D9B5A98B5C6BE0CE9B759D50A3FB95D73E6A0DAE8913A2D739, the actual first Inspector edit changed ReverseAsBrake true to false in the live JSON. Ctrl+Z and Save restored true; after explicitly focusing the central text editor, Ctrl+Y and Save produced false (SHA-256 8B281B6E04AA69E0E39AC6F39F7A8F26A54555A54207F6795694DC4A0A84069C). Ctrl+Z and Save restored the exact original SHA-256 D58776ECD5ADF70DF7D4AC26F36311E2EBF98365EEB974C3677A0717EC0D1707. The full linked multi-document/file-operation Undo gate remains open.
- Deleting the temporary smoke asset succeeded, but CPS briefly retained its removed item and threw from the Link property getter during inventory refresh. Logical Link is now read only while the source exists; missing-source state remains visible until project reevaluation.

The temporary AssetWorkspaceSmoke.cgeasset was created through the actual UI, edited through Inspector, and saved through native Ctrl+S. The file remained unchanged on disk before Save. It was restored to its exact initial bytes, closed and deleted through the reviewed asset operation. The physical source is absent. CPS retained the evaluated item and the list/Inspector reported MissingSource without a Link-property exception. Removing this stale evaluated item immediately remains a project-notification integration concern. Explicit layout with the same build retained the IDE's 3086 by 1249 window size and the existing Output/Assets tab group. For editing verification only, the bottom splitter was manually enlarged from 192 to 385 DIP; that manual action is not evidence that the 280 DIP placement requirement is implemented.

Four additional interruption tests failed before the recovery fix: pending case-only rename, move before metadata update, original-file recreation with identical bytes, and the case-rename intermediate file. Recovery now checks all participants before mutation, accepts known per-file before/after states, and rejects recreated/conflicting originals. Source creation/copy/rewrite and metadata publication stage bytes before atomic moves. Failed applies use that same guarded recovery path and retain an Applying journal when safe rollback is impossible. Content 64/64 passes in Debug and Release (`asset-workspace-recovery-after-*.log`). These tests exercise simulated process-stop states, not power-loss durability or the complete native linked-Undo crash matrix.

An additional read-only-provider copy test reproduced File.Copy carrying the ReadOnly attribute to a project copy. The staged copy now clears only its own ReadOnly attribute before publication; the provider source is unchanged. A further test reproduced an Undo request with a mismatched operation kind being accepted. Reverse now revalidates the journal request and requires matching kind, directory flag, roots, paths, expected hashes and content before mutation. Final Content **66/66 passes in Debug and Release** (`asset-workspace-content-final-*.log`). The whole-solution snapshots below contain 65 tests because they predate this final identity guard; final packages are rebuilt after it.

MissingSource selections now disable open, reference analysis and organize actions, and the Inspector stops before requesting typed data. The existing Contracts capability flags determine open/organize availability. This last UI guard is CodeVerified pending another actual shell pass. English state labels use readable words instead of enum identifiers; unavailable-import and folder-operation messages describe the supported user actions without internal milestone/service names.

## Content thumbnails (2026-10-26)

- Feature: F-L8-035. Status: Implementing; verification: Unverified pending build/runtime acceptance.
- Static implementation: `AssetThumbnailRequest`, bounded `AssetThumbnailService`, weak row images and virtualized unsubscribe; geometry/basic-color render snapshots in `EditorPreview.Host/AssetThumbnailWorker`; source-image decoding only in `Cook.Cli/ThumbnailImageDecoder` and `ImageThumbnailCommand`; provider-free PNG utility linked by the two tools.
- Model/scene geometry uses the existing L8 model facade and scene analyzer. No game world is started. Empty group placeholders are omitted while descendant transforms remain. Primitive GLBs are derived temporary files, never source Assets writes. Child identities are accepted by the row/cache API; child previews remain unavailable until their real artifact/selector is connected.
- CPU-only regression sources cover source contracts, BGRA/row-pitch conversion, PNG round-trip/aspect scaling, read-only image requests, previous-output preservation and placeholder-free descendant transforms. Source syntax parsing is a static check only; it does not prove compilation or behavior.
- Build, test execution, GPU capture and Visual Studio acceptance were deliberately not run while the user's RAM was approximately 87%. No runtime verification is claimed. Complete these gates after resource authorization, and call `AssetThumbnailService.Invalidate()` from inventory refresh so dependent meshes/images cannot retain stale scene pictures.
