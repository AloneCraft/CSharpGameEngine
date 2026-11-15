using System.Collections.Immutable;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Component の有効マスクを、64 ビットの語の並びで扱う(ADR-0079。上限は <see cref="GameSystemIndexAssigner.MaxComponentTypes"/>)。
/// 語の名前は Low・High・Word2・Word3… で、1〜128 種類(1 語と 2 語)の生成コードは語数を一般化する前と同じ文字列になる。
/// </summary>
internal static class ComponentMask {
    internal const int BitsPerWord=64;

    internal static int WordCount(int ComponentTypeCount)=>Math.Max(1,(ComponentTypeCount+BitsPerWord-1)/BitsPerWord);

    internal static string WordName(int Word)=>Word switch{
        0=>"Low",
        1=>"High",
        _=>"Word"+Word,
    };

    internal static int WordOf(int ComponentIndex)=>ComponentIndex/BitsPerWord;

    internal static int BitOf(int ComponentIndex)=>ComponentIndex%BitsPerWord;

    internal static ImmutableArray<ulong> Build(IEnumerable<int> ComponentIndices,int WordCount){
        var Words=new ulong[WordCount];
        foreach(var Index in ComponentIndices){ Words[WordOf(Index)]|=1UL<<BitOf(Index); }
        return [.. Words];
    }

    /// <summary><paramref name="Available"/>のすべてのビットに<paramref name="Required"/>が設定されているかどうか。</summary>
    internal static bool Contains(ImmutableArray<ulong> Available,ImmutableArray<ulong> Required){
        for(var Word=0;Word<Required.Length;Word++){
            if((Available[Word]&Required[Word])!=Required[Word]){ return false; }
        }
        return true;
    }

    internal static string Literal(ulong Word)=>$"0x{Word:X16}UL";

    /// <summary>「0x…UL, 0x…UL」：引数として渡すワード列。</summary>
    internal static string Literals(ImmutableArray<ulong> Words)=>string.Join(", ",Words.Select(Literal));

    /// <summary>「ulong Low, ulong High」：ワードごとに1つのパラメーターを置き、名前には任意の接頭辞を付けられる。</summary>
    internal static string Parameters(int WordCount,string Prefix="")=>
        string.Join(", ",Enumerable.Range(0,WordCount).Select(Word=>$"ulong {Prefix}{WordName(Word)}"));
}
