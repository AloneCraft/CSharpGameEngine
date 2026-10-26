using System;
using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension;

/// <summary>初回レイアウトに参加できる tool window の種類。</summary>
internal enum InitialLayoutToolWindowKind {
    Hierarchy,
    SceneDesigner,
    Inspector,
    ComponentPalette,
}

/// <summary>Visual Studio のキーボードフォーカスを奪わない表示ポリシー。</summary>
internal enum ToolWindowPresentation {
    NonActivating,
}

/// <summary>初回レイアウトに含まれる、変更不可の tool window 項目。</summary>
internal sealed class InitialLayoutToolWindowSpec {
    internal InitialLayoutToolWindowSpec(
        InitialLayoutToolWindowKind Kind,
        int Width,
        ToolWindowPresentation Presentation){
        this.Kind=Kind;
        this.Width=Width;
        this.Presentation=Presentation;
    }

    internal InitialLayoutToolWindowKind Kind { get; }

    internal int Width { get; }

    internal ToolWindowPresentation Presentation { get; }
}

/// <summary>
/// 初回レイアウトの計画。Scene Designer はコードエディターで明示的に選択した IScene が必要なため含めません。Inspector（M0031）は Hierarchy と並べて含めます。Scene Designer と異なり通常の WPF panel で、先行して作成しても D3D12/HWND のコストがなく、「ノードを選択してください」という空の状態も Hierarchy と同様に正当だからです。
/// </summary>
internal static class InitialLayoutToolWindowPlan {
    private static readonly IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindowsValue=
        Array.AsReadOnly(new[]{
            new InitialLayoutToolWindowSpec(
                InitialLayoutToolWindowKind.Hierarchy,
                Width:300,
                ToolWindowPresentation.NonActivating),
            new InitialLayoutToolWindowSpec(
                InitialLayoutToolWindowKind.Inspector,
                Width:300,
                ToolWindowPresentation.NonActivating),
            // M0052: パレットはInspectorと同じ右側だが、下段へ置いて<b>タブ結合させない</b>。
            // ドラッグ中に隠れてしまうドロップ先は成立しないため、両方が同時に見えている必要がある。
            new InitialLayoutToolWindowSpec(
                InitialLayoutToolWindowKind.ComponentPalette,
                Width:300,
                ToolWindowPresentation.NonActivating),
        });

    internal static IReadOnlyList<InitialLayoutToolWindowSpec> ToolWindows => ToolWindowsValue;
}