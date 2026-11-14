# ゲームの深度方式

- Decision: [ADR-0096](../Decisions/ADR-0096-Project-Depth-Mapping.md)
- Implementation evidence: `CSharpGameEngine.RenderProfile.props`, `RenderViewDepth`, `RenderViewSource`, `RenderViewPolicy`, `EngineHost.SubmitCamera`
- Status: CPU/GPU契約とSourceのFloat/Identityビルドは検証済み。制作・配布全体の受け入れはM0128で記録する。

## プロジェクト設定

csprojで `CgeDepthMapping` を指定する。選択肢は `Logarithmic` と `Identity`。省略時はLogarithmic。[数値の計算型](Numeric-Configuration.md)とは独立して選ぶ。

```xml
<PropertyGroup>
  <CgeNumericMode>Float</CgeNumericMode>
  <CgeDepthMapping>Identity</CgeDepthMapping>
</PropertyGroup>
```

Logarithmicは既存の対数距離の深度を使う。Identityは通常の透視投影の深度をそのまま使い、対数の写像を適用しない。Identityの深度精度は通常のfloat透視投影の近平面・遠平面に依存する。大きな範囲の対数深度の精度保証をIdentityへ適用しない。

Identityのゲーム出力と中間生成物には `identity` の階層を追加する。数値型のDLLと物理キャッシュは描画設定を含まない。設定の不正値はビルドエラーになる。

## 描画契約

ゲームのカメラ提出は条件付きコンパイルで選択する。Renderingは明示的なIdentity作成APIを持ち、透視と正射影をCPUで区別する。IdentityのGPUパケットは既存Stageシェーダーの直接深度経路を選択するため、別のStageグラフや実行時のシェーダー生成を必要としない。

Identityでは描画行列と復元用行列を同じにし、行列の有限性・逆行列・透視投影、カメラ、露出、任意の基底を検証する。カメラ入力は描画面の変更とデバイス復旧で再解決する。対数の遷移距離はIdentityの検証に使わない。対数観測者の補助表示はIdentityゲームへ提出しない。GI内部の撮影と影のビューは各パスの明示的な方式を使う。

実装と検証の詳細は [M0128](../Milestones/M0128-Static-Production-Flow.md) を参照。

日本語の実設定画面で、数値型をDoubleにしたままLogarithmicからIdentityへ切り替え、csprojの`CgeDepthMapping`へ保存できることを確認済み。SDK0.1.44のDouble/IdentityはVisual Studio用MSBuildでビルド・自己完結発行し、元モデルなしでWARP12フレーム、D3D12エラー0を確認した。最終候補でのIDE起動・再開の証拠はM0128に分けて記録する。
