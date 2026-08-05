# ADR-0028: ECSゲームプレイ層のRUF境界配置

## ステータス
承認済み

## 背景

[[ADR-0026-Camera-Geometry-Ruf-Integration]]でCamera/GeometryをRUF化した後、同じ「Y=Up、
Z=Forward(Yaw=0時)」規約を独立に前提としていた`UserGameProject/Systems/PlayerControlSystem.cs`・
`CameraSystem.cs`にも同じ扱いを適用する。`UserGameProject/Components.cs`の`Position`/`Rotation`
等はAoS/SoAソースジェネレータ(ADR-0024)の都合上バラのfloatフィールドを持つ独自structであり、
ユーザーは「ECSストレージ自体は変えない」という意図を示していたため、RUF境界をどこに置くかが
本ADRの主題。

## 決定事項

### ECSコンポーネントのフィールドレイアウトは変更しない
`Position`/`Rotation`/`Velocity`/`Scale`のバラfloatフィールド構成は無改修。`RufVector`/
`RufQuaternion`をコンポーネントのフィールド型に採用すると、`[GenerateEntity]`ソースジェネレータ
(`CSharpGameEngine.CodeAnalysis`)がこれらの型をblittableフィールドとして正しく扱えるかという
別の大きな検証が必要になり、今回のスコープを大幅に超えるため見送った。

### RUF境界はSystem関数の内部(冒頭で読み出し、末尾で書き戻す)に置く
`PlayerControlSystem.Update`: `flatForward`/`flatRight`/身体の向きQuaternionを`RufVector`/
`RufQuaternion`(`Spatial<UnityCoordinateConvention>.RightFromUpForward`含む、
`Camera.GetFlatForward`/`GetFlatRight`と同じ実装パターン)で計算し、`CharacterMotion.Compute`
(`PlayerController.cs`)へ渡す直前で`Vector3`へ変換する。`CharacterMotion.Compute`自体は
「GPU/World/Window非依存のテスト容易性」という既存の設計意図(XMLコメント)を尊重し無改修のまま。

`CameraSystem.Update`: `EyeHeight`の加算を`RufVector.Up`経由の計算に置き換えた
(`position + RufVector.Up * EyeHeight`)。

いずれも`UnityCoordinateConvention`が恒等写像であるため、計算結果は既存の生Vector3実装と
ビット単位で一致する。

## 帰結
- 既存`tests/UserGameProject.Tests/PlayerControlSystemTests.cs`・`CharacterMotionTests.cs`は
  無改修のまま全件成功した。
- Camera.cs(ADR-0026)・PlayerControlSystem.cs(本ADR)の両方が「Yaw軸周りのRUF回転→
  RightFromUpForward」という同じ実装パターンを使うようになり、以前は独立に再実装されていた
  同種の計算が統一された。

## 検証結果(実際に実行して確認)
```
dotnet test tests/UserGameProject.Tests/...csproj  → 41件成功(既存、無改修)
dotnet build CSharpGameEngine.slnx -c Debug/Release → 成功、警告0/エラー0
```

## 関連機能
- F-L9-004 — ECS入力抽象化とゲームプレイSystems群(既存ID更新)
