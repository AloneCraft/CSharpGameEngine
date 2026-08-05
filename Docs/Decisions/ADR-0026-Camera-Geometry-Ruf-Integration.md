# ADR-0026: Camera/GeometryのRUF内部実装統合

## ステータス
承認済み

## 背景

ADR-0025でRUF(Right/Up/Forward)意味座標系のコア契約・Unity/Unreal/Blender Convention・Preset選択機構が
完成したが、既存の`CSharpGameEngine.Graphics.D3D12/Camera.cs`・`CSharpGameEngine.Geometry/
ProceduralMeshFactory.cs`・`ModelRenderer.cs`はRUFと無関係に生の`System.Numerics.Vector3`で完結して
いた。ユーザーは「次の実装」として、これらの内部実装までRufVector/RufQuaternion経由に置き換える
フルスコープを明示的に選択した(仕様書自身が推奨する「Renderer内部は生Vectorのままでよい」という
最小スコープではなく、あえて広い方)。

調査の結果、これら3ファイルは独立に「Y=Up、Z=Forward(Yaw=0時)、X=Right」という同じ規約を前提に
しており、これは既存の`UnityCoordinateConvention`(R=+X,U=+Y,F=+Z,LeftHanded)と数値的に完全一致
(恒等変換)することが判明した。ユーザーはこれを正としてADR-0017(「エンジン空間=glTF空間(右手系)」)を
上書きすることを選択した(詳細は[[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]参照)。

## 決定事項

### エンジンのRUF規約は`UnityCoordinateConvention`に決め打ちする
`CSharpGameEngine.Geometry`・`CSharpGameEngine.Graphics.D3D12`・`UserGameProject`はいずれも
`CSharpGameEngine.Spatial.Abstractions`+`CSharpGameEngine.Spatial.Unity`への直接`ProjectReference`を
追加した。`SpatialPresetSelection.props`(Preset可変選択の仕組み)は使わない — 今回のスコープは
「既存Rendererの内部実装をRUF型で置き換える」ことであり、Preset可変化(将来Unreal/Blenderを
選べるようにする)は対象外。共有ファサード型(`EngineSpatial`のようなもの)は作らず、
`Spatial<UnityCoordinateConvention>`を各ファイルから直接呼び出す — 当初「`Graphics.D3D12`は
既に`Geometry`に依存しているので共有できる」と想定したが、実際には依存関係が無いことが
`.csproj`を直接確認して判明した(`ProceduralMeshFactory`の実利用者は`tools/CSharpGameEngine.
Primitive.Cli`のみ)ため、この想定は採用しなかった。

### `ProceduralMeshFactory.cs`: 全8プリミティブをRUF空間で再構築
`CreatePlane`/`CreateBox`/`CreateSphere`/`CreateCylinder`/`CreateCone`/`CreateCapsule`/`CreateTorus`/
`CreateDisk`の頂点・法線計算を`RufVector`で行い、最終的な`ProceduralMeshData`(`Vector3[]`)への
格納直前だけ`Spatial<UnityCoordinateConvention>.ToXyz`で変換する。`UnityCoordinateConvention`が
恒等写像(`new(R,U,F)`というコピーのみ)であるため、演算順序を変えない限り出力は完全に不変。
実際に`tests/CSharpGameEngine.Geometry.Tests/ProceduralMeshFactoryTests.cs`(30件、法線の厳密一致
チェック含む)を無改修のまま実行し、全件成功することで実証した。

`CreateBox`の面中心/頂点計算(`normal * half`という成分ごとのスケール)は`RufVector`が
コンポーネント間の乗算演算子を持たないため、ファイル内に`ScaleComponents`という小さなprivateヘルパー
(`R*R, U*U, F*F`)を追加した(`Spatial.Abstractions`側に汎用の成分積演算子を追加する変更は、
既に完成・文書化済みのコア型への不要な拡張になるため見送った)。

### `Camera.cs`: `GetFlatRight`/`GetForward`/`GetFlatForward`をRUF経由に書き換え
- `GetFlatRight()`: `Vector3.Cross(Vector3.UnitY, GetFlatForward())`を`Spatial<UnityCoordinateConvention>.
  RightFromUpForward(RufVector.Up, forwardRuf)`へ置換。`UnityCoordinateConvention`が恒等写像+
  LeftHandedであるため、この2つは**ビット単位で同一の結果**になることを検算・実測の両方で確認した。
  これによりXMLコメント「ADR-0004 §6により実機検証待ちの暫定値」は「RUF経由で数式としては証明済み
  (実機フィーリングの検証は引き続き別課題)」という記述に更新した。
- `GetForward()`/`GetFlatForward()`: `RufQuaternion.FromAxisAngle(RufVector.Up, Yaw) *
  RufQuaternion.FromAxisAngle(RufVector.Left, Pitch)`を`RufVector.Forward`へ適用する形に書き換えた。
  Pitch軸には`RufVector.Right`ではなく`RufVector.Left`を使う必要がある(標準的な軸角回転公式で
  検算した結果、`Right`軸を使うと符号が反転しXMLコメントが宣言する「pitch tilts toward +Y」と
  逆になることを確認した)。
- 三角関数の直接計算からQuaternion合成に変わるため、丸め誤差の入り方が変わりビット単位では
  一致しなくなる(数学的には同値)。移行の安全網として、旧式(手書き三角関数)を独立に再実装した
  シャドウテスト(`CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep`、
  `GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep`)を恒久的に追加し、
  Yaw/Pitchの広い範囲で両実装が一致することを実測確認した。
- `GetView()`/`GetProjection()`自体(`Matrix4x4.CreateLookAt`/`CreatePerspectiveFieldOfView`の
  呼び出し)はRUF化していない。D3Dのビュー/クリップ空間規約は`Spatial-RUF.md`が意図的に
  「Rendering境界」として未確定のままにしている領域であり、今回のスコープ(既存Rendererの内部実装を
  RUF型で置き換える)とは別の新規設計判断を要するため対象外とした。`GetView()`に渡す`eye`/
  `lookAtPoint`の計算(`GetForward()`経由)だけがRUF化されている。

### `ModelRenderer.cs`の重複LookAt構築を`Camera.BuildViewMatrix`で解消
`ModelRenderer.UpdateTransformConstantBuffer`が独自に持っていた`Matrix4x4.CreateLookAt(eye,
_boundsCenter, Vector3.UnitY)`呼び出しは、`Camera`に追加した静的ヘルパー`BuildViewMatrix(eye,
target, up)`(`Matrix4x4.CreateLookAt`への薄い委譲)を共有する形にした。`ModelRenderer`独自の
「バウンディングスフィアへ3/4角度で自動フィットするeye計算」自体は`Camera`のYaw/Pitch状態とは
無関係の別機能なので、無理に統合せず残した。

## 帰結
- 既存`tests/CSharpGameEngine.Graphics.D3D12.Tests/CameraTests.cs`・
  `tests/CSharpGameEngine.Geometry.Tests/ProceduralMeshFactoryTests.cs`は無改修のまま全件成功。
- `StageGpuIntegrationTests.cs`(WARP実描画)も無改修のまま全件成功し、Camera/Geometry双方の
  変更がGPU統合レベルで視覚的破綻を起こしていないことを確認した。
- Rendering境界(View/Projection規約、NDC、Depth Range、Winding既定値)は依然として未確定のまま
  (`Spatial-RUF.md`「未確定事項」参照)。

## 検証結果(実際に実行して確認)
```
dotnet test tests/CSharpGameEngine.Geometry.Tests/...csproj        → 30件成功(既存、無改修)
dotnet test tests/CSharpGameEngine.Graphics.D3D12.Tests/...csproj  → 60件成功(既存58+新規シャドウ2)
dotnet build CSharpGameEngine.slnx -c Debug/Release                → 成功、警告0/エラー0
```

## 関連機能
- F-L0-001 — `Camera`(既存ID更新)
- F-L2-002 — `CSharpGameEngine.Geometry`(既存ID更新)
- F-L0-006 — `Camera.BuildViewMatrix`共有ヘルパー(新規)
