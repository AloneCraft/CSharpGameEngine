namespace CSharpGameEngine.Numerics;
/// <summary>192bitの2の補数表現。Low/Middleは下位桁、Highは符号付き上位桁。</summary>
public readonly record struct Int192(ulong Low,ulong Middle,long High);
