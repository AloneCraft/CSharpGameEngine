using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene Designer の WPF 領域へネイティブ描画 Host を attach してよい状態を表します。
/// 未選択時に <c>HwndHost</c> を置いたまま非表示にすると、Win32 の airspace が WPF の空状態を
/// 覆い、以前のフレームが残像として見えるためです。
/// </summary>
internal enum SceneDesignerHostState {
    Empty,
    SceneReady,
    SceneUnavailable,
}

internal static class SceneDesignerHostAttachmentPolicy {
    internal static bool ShouldAttach(SceneDesignerHostState State)=>State==SceneDesignerHostState.SceneReady;
}

/// <summary>現在の Roslyn Project 内で選択できる Scene の不変な表示項目です。</summary>
internal sealed class ProjectSceneCatalogEntry {
    internal ProjectSceneCatalogEntry(SceneKey Key,string ClassName,string FilePath,int DeclarationPosition){
        this.Key=Key;
        this.ClassName=ClassName??throw new ArgumentNullException(nameof(ClassName));
        this.FilePath=FilePath??throw new ArgumentNullException(nameof(FilePath));
        this.DeclarationPosition=DeclarationPosition;
    }

    internal SceneKey Key { get; }
    internal string ClassName { get; }
    internal string FilePath { get; }
    internal int DeclarationPosition { get; }
    // WPF の DisplayMemberPath は public CLR property だけを参照する。internal のままだと
    // 項目そのものはあるのに文字列だけ描画されず、空白行の ComboBox になってしまう。
    public string DisplayName { get;private set;}=string.Empty;

    internal void SetDisplayName(string Value)=>this.DisplayName=Value;
}

internal sealed class ProjectSceneCatalog {
    internal ProjectSceneCatalog(string ProjectId,IReadOnlyList<ProjectSceneCatalogEntry> Entries){
        this.ProjectId=ProjectId;
        this.Entries=Entries;
    }

    internal string ProjectId { get; }
    internal IReadOnlyList<ProjectSceneCatalogEntry> Entries { get; }
}

/// <summary>Project Scene 一覧の安定順と重複時の表示名を、WPF/Roslyn から切り離して固定します。</summary>
internal static class ProjectSceneCatalogPresentation {
    internal static IReadOnlyList<ProjectSceneCatalogEntry> Create(string ProjectId,IEnumerable<ProjectSceneCatalogEntry> Entries){
        ProjectSceneCatalogEntry[] Ordered=Entries
            .Where(Entry=>string.Equals(Entry.Key.ProjectId,ProjectId,StringComparison.Ordinal))
            .OrderBy(Entry=>Entry.FilePath,StringComparer.OrdinalIgnoreCase)
            .ThenBy(Entry=>Entry.DeclarationPosition)
            .ToArray();

        foreach(IGrouping<string,ProjectSceneCatalogEntry> Group in Ordered.GroupBy(Entry=>Entry.ClassName,StringComparer.Ordinal)){
            bool Duplicate=Group.Skip(1).Any();
            foreach(ProjectSceneCatalogEntry Entry in Group){
                Entry.SetDisplayName(Duplicate
                    ?Entry.Key.ClassFullyQualifiedName.Replace("global::",string.Empty)
                    :Entry.ClassName);
            }
        }

        return Ordered;
    }
}
