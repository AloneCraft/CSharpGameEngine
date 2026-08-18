using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// M0049: `*.scene.json`のデシリアライズ先POCO。<see cref="System.Text.Json"/>専用 — Roslynの
/// <c>ISymbol</c>は一切持たない(<see cref="SceneJsonAnalyzer"/>が<see cref="Microsoft.CodeAnalysis.
/// Compilation"/>と突き合わせてこれらを<see cref="SceneEmitModel"/>へ変換する)。
///
/// <para>
/// 値の解釈はJSON側にタグを持たせず、対象フィールド/プロパティの宣言型で決める
/// (<see cref="SceneJsonAnalyzer"/>参照)。enumは文字列でメンバー名、それ以外はJSONネイティブ型
/// (string/number/bool)をそのまま使う。
/// </para>
/// </summary>
internal sealed class SceneJsonDocument {
    [JsonPropertyName("namespace")]
    public string?Namespace { get; set; }

    [JsonPropertyName("className")]
    public string ClassName { get; set; } = "";

    /// <summary>省略 = startup sceneではない。</summary>
    [JsonPropertyName("startupOrder")]
    public int?StartupOrder { get; set; }

    [JsonPropertyName("root")]
    public SceneJsonNode?Root { get; set; }
}

internal sealed class SceneJsonNode {
    [JsonPropertyName("name")]
    public string?Name { get; set; }

    [JsonPropertyName("components")]
    public List<SceneJsonComponent>?Components { get; set; }

    [JsonPropertyName("children")]
    public List<SceneJsonNode>?Children { get; set; }
}

internal sealed class SceneJsonComponent {
    /// <summary>Component構造体の完全修飾名(<c>global::</c>接頭辞なし)。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("members")]
    public Dictionary<string,System.Text.Json.JsonElement>?Members { get; set; }
}
