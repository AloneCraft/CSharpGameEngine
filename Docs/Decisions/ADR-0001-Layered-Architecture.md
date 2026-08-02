# ADR-0001: レイヤードアーキテクチャ

## ステータス
採用済み

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
