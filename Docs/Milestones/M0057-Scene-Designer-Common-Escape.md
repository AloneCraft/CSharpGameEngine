# M0057: Scene Designer共通Esc操作

- Status: Implemented
- Date: 2026-08-24

## Goal

Scene View、Hierarchy、Inspector、Component Paletteのいずれにフォーカスがあっても、EscをVisual Studioの文書タブ操作へ漏らさず、局所の取消を優先してから共通Scene選択を解除する。

## Implemented

- `SceneEditorEscapeRouter`を4 panel rootへ導入した。
- WPF preview eventとHwndSource `WM_KEYDOWN`/`WM_KEYUP` hookを併用し、フォーカス中だけEscを消費する。
- key repeatは一度だけ意味処理し、KeyUp、focus loss、Unload、Disposeで解除する。
- Scene ViewはCamera/selection/gizmo transactionを先に取消し、Hierarchyはrename、Inspectorはtext/ComboBox、Component Paletteはsearchを先に取消す。
- 選択解除は`SceneSelectionService.TryClearCurrent`または既存Hierarchy publication経路で一度だけ発行する。

## Excluded

- EscでPhysics panel、Camera Preview、Scene Designer documentを閉じる機能。
- Visual Studio全体へのEscショートカット登録。

## Verification

- `dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj -c Debug --no-restore`
- Result: VSIX 969 passed, 0 failed per configuration; full solution 1,575 passed, 0 failed per configuration.
- Experimental Instanceでの「選択解除後も別タブがアクティブ化しない」実機確認は未実施。

## Feature ID

- F-L8-017

## Conformance

自動テスト範囲ではMatches。HwndSourceからVisual Studio command routingを遮断する実機確認が完了するまでRuntimeVerifiedへ昇格しない。