namespace CSharpGameEngine.VSExtension;

/// <summary>VSCTのDynamicItemStartへ割り当てる連番コマンドID範囲。</summary>
internal static class DynamicSceneMenuIdRange {
    internal const int MaximumItemCount=128;

    internal static bool Contains(int FirstCommandId,int CommandId)=>
        CommandId>=FirstCommandId&&CommandId<FirstCommandId+MaximumItemCount;
}
