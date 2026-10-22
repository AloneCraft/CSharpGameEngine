namespace CSharpGameEngine.Authoring;
/// <summary>
/// ユーザーが定義するデータの型(ADR-0079。UE の <c>UDataAsset</c> に当たる)。値は <c>Assets/**/*.cgeasset</c> のアセットに書き、
/// Source Generator がシーンと同じ規則で C# にコンパイルして、<c>Assets.Cars.SportsCar</c> のような型付きの静的メンバーにする。
/// 文書は <c>{ "schemaVersion": 3, "type": "型の完全名", "members": { … } }</c>(シーンの Component 1 つと同じ書き方)。
/// アセットはデータだけを持ち、処理は持たない。
/// </summary>
[AttributeUsage(AttributeTargets.Struct|AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class DataAssetAttribute:Attribute {
}
