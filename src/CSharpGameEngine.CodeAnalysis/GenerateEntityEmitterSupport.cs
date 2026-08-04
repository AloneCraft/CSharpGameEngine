namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Common/Aos/Soaの3つのEmitterが共通で必要とする、Hint Name構築とnamespace/containing型の
/// 開始・終了行の組み立てを提供する。CodeAnalysis.Abstractionsのような別プロジェクトへ切り出すほどの
/// 汎用性はまだない(この1機能の3ファイル間だけで閉じた共有)ため、同一プロジェクト内のファイルとする。
/// </summary>
internal static class GenerateEntityEmitterSupport
{
    internal static string BuildHintName(GenerateEntityAnalysisResult result, string suffix)
    {
        List<string> parts = [];

        if (result.Namespace is not null)
        {
            parts.Add(result.Namespace);
        }

        foreach (GenerateEntityContainingTypeModel containingType in result.ContainingTypeChain)
        {
            parts.Add(containingType.Name);
        }

        parts.Add(result.StructName);
        parts.Add(suffix);

        return string.Join(".", parts) + ".g.cs";
    }

    internal static void OpenContainingScope(List<string> lines, GenerateEntityAnalysisResult result)
    {
        if (result.Namespace is not null)
        {
            lines.Add("");
            lines.Add($"namespace {result.Namespace};");
        }

        foreach (GenerateEntityContainingTypeModel containingType in result.ContainingTypeChain)
        {
            lines.Add("");
            lines.Add($"partial {containingType.Keyword} {containingType.Name}");
            lines.Add("{");
        }
    }

    internal static void CloseContainingScope(List<string> lines, GenerateEntityAnalysisResult result)
    {
        for (int i = 0; i < result.ContainingTypeChain.Length; i++)
        {
            lines.Add("}");
        }
    }
}
