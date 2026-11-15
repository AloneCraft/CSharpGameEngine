using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// ソースで宣言されたコンポーネント<see langword="struct"/>と、所有プロジェクト内のアイコンを関連付ける。
/// 値は意図的にアセット作成用メタデータだけとし、Visual Studio拡張が利用する。ランタイムやシーン生成の動作には影響しない。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SceneComponentIconAttribute(string ProjectRelativePath) : Attribute {
    /// <summary>
    /// プロジェクト相対の<c>.svg</c>または<c>.png</c>アセットパスで属性を初期化する。
    /// パスは、属性の記述時にアセット作成UIが、アイコンの描画時にリーダーが検証する。
    /// 手書きの値が無効でも、標準のコンポーネントアイコンに安全にフォールバックする。
    /// </summary>
    /// <summary>このコンポーネント用に選択された、プロジェクト相対のソースアセットパス。</summary>
    public string ProjectRelativePath { get; }=ProjectRelativePath??throw new ArgumentNullException(nameof(ProjectRelativePath));
}
