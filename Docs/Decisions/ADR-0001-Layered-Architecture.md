# ADR-0001: レイヤードアーキテクチャ

## ステータス
採用済み。[[ADR-0034-Project-Consolidation]]で注記を追加(下記「ADR-0034による注記」)。

## コンテキスト
`CSharpGameEngine` では、ランタイム・静的コンパイル/Bake・オーサリングツールをそれぞれ独立して進化させられるアーキテクチャが必要である。

## 決定
次の10レイヤーモデルを採用する。
- L0 Platform / Native Backends
- L1 Core Kernel
- L2 Type Schema / Identity
- L3 ECS / Job Runtime
- L4 Domain Servers
- L5 Runtime Feature Modules
- L6 Runtime Artifacts
- L7 Static Compiler / Bake
- L8 Visual Studio Authoring Environment
- L9 Game / Application

依存ルール:
- 上位レイヤーは下位レイヤーに依存できる。
- 下位レイヤーは上位レイヤーに依存してはならない。

追加制約:
- Visual Studio 拡張はエンジン/コンパイラ/CLI 契約の薄いフロントエンドに留める。
- ランタイムレイヤーは Roslyn および Visual Studio SDK を参照しない。
- レイヤー番号は文書/ソリューション整理用メタデータであり、名前空間へ埋め込まない。

## 根拠
- Visual Studio 拡張を薄く保つことでツールロックインを防ぎ、エンジン実行経路の独立性を維持できる。
- ランタイムから Roslyn/VS SDK を除外することで、配布ランタイムに不要なホスト/ツールチェーン依存を持ち込まない。
- 名前空間にレイヤー番号を入れないことで、ドメイン中心のAPIを維持し、将来のリファクタリング時の変更範囲を抑えられる。

## 結果
- ランタイム、Bake、オーサリングを CI/CLI 自動化で分離運用できる。
- モジュール追加時に依存方向を検証しやすくなる。

## ADR-0034による注記(レイヤーはアセンブリでは強制されない)

[[ADR-0034-Project-Consolidation]]が`src/`を16プロジェクトから5へ統合した結果、
**レイヤー境界とアセンブリ境界は一致しなくなった**。本ADRのレイヤーモデル自体は無変更である。

- 統合で**下位が上位に依存する辺は1本も生まれていない**。`Import → Core`・`Windows → Core`は
  いずれも上向きであり、依存方向の規則は守られている。
- `CSharpGameEngine.Windows`(旧`Graphics.D3D12` + `Platform.Windows` + `Compiler.Hlsl`)は
  3つともL0なので、**同一レイヤー内の統合**であり影響はない。
- ただし`CSharpGameEngine.Core`はL1(診断・RUF)とL2(`CameraBasis`・`ProceduralMeshFactory`)を
  跨ぐため、**L1とL2の境界はアセンブリでは強制されなくなった**。

したがってレイヤーの追跡単位はアセンブリではなく**Feature ID**である
(`Docs/Implementation-Status.md`)。「ランタイムレイヤー(L0-L6)は Roslyn や Visual Studio SDK を
参照してはならない」という規則は、`src/`の5プロジェクトのうちRoslynを参照するのが
`CSharpGameEngine.CodeAnalysis`(L7/L8側)だけである以上、引き続きプロジェクト参照で強制されている。
