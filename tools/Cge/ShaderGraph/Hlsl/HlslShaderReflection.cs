using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Compiler.Hlsl;
public sealed record HlslVariableLayout(string Name,uint Offset,uint Size,string Class,string Type,uint Rows,uint Columns,uint Elements);
public sealed record HlslConstantBufferLayout(string Name,uint Size,IReadOnlyList<HlslVariableLayout>Variables);
/// <summary>L7 のコンパイラーリフレクションを読み取る。ランタイムアセンブリからこのコードを読み込むことはない。</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
public static class HlslShaderReflection {
    public static unsafe IReadOnlyList<HlslConstantBufferLayout>ReadConstantBuffers(byte[]Data){
        ArgumentNullException.ThrowIfNull(Data);
        if(Data.Length==0){ throw new ArgumentException("Reflection data cannot be empty.",nameof(Data)); }
        IDxcUtils*Utils=null;
        ID3D12ShaderReflection*Reflection=null;
        try {
            var Clsid=CLSID.CLSID_DxcUtils;
            Check(DxcCreateInstance(&Clsid,__uuidof<IDxcUtils>(),(void**)&Utils),"Create reflection utilities");
            fixed(byte*Bytes=Data){
                DxcBuffer Buffer=new(){ Ptr=Bytes,Size=(nuint)Data.Length,Encoding=0 };
                Check(Utils->CreateReflection(&Buffer,__uuidof<ID3D12ShaderReflection>(),(void**)&Reflection),"Read shader reflection");
            }
            D3D12_SHADER_DESC Shader;
            Check(Reflection->GetDesc(&Shader),"Read shader description");
            List<HlslConstantBufferLayout>Buffers=[];
            for(uint Index=0;Index<Shader.ConstantBuffers;Index++){
                var Native=Reflection->GetConstantBufferByIndex(Index);
                D3D12_SHADER_BUFFER_DESC Description;
                Check(Native->GetDesc(&Description),"Read constant buffer description");
                List<HlslVariableLayout>Variables=[];
                for(uint VariableIndex=0;VariableIndex<Description.Variables;VariableIndex++){
                    var Variable=Native->GetVariableByIndex(VariableIndex);
                    D3D12_SHADER_VARIABLE_DESC Value;
                    Check(Variable->GetDesc(&Value),"Read constant variable description");
                    D3D12_SHADER_TYPE_DESC Type;
                    Check(Variable->GetType()->GetDesc(&Type),"Read constant variable type");
                    Variables.Add(new(Name(Value.Name),Value.StartOffset,Value.Size,Type.Class.ToString(),Type.Type.ToString(),Type.Rows,Type.Columns,Type.Elements));
                }
                // 構造化バッファは、要素の構造体 1 つ（$Element）を持つバッファとして現れる。構造体のメンバーを変数として返すので、
                // 定数バッファと同じ比べ方で要素の配置を確かめられる。
                if(Description.Type==D3D_CBUFFER_TYPE.D3D_CT_RESOURCE_BIND_INFO&&Description.Variables==1){
                    var Element=Native->GetVariableByIndex(0)->GetType();
                    D3D12_SHADER_TYPE_DESC ElementType;
                    Check(Element->GetDesc(&ElementType),"Read structured buffer element type");
                    if(ElementType.Class==D3D_SHADER_VARIABLE_CLASS.D3D_SVC_STRUCT){
                        Variables.Clear();
                        for(uint MemberIndex=0;MemberIndex<ElementType.Members;MemberIndex++){
                            D3D12_SHADER_TYPE_DESC Member;
                            Check(Element->GetMemberTypeByIndex(MemberIndex)->GetDesc(&Member),"Read structured buffer member type");
                            Variables.Add(new(Name(Element->GetMemberTypeName(MemberIndex)),Member.Offset,Member.Rows*Member.Columns*sizeof(float)*Math.Max(Member.Elements,1u),Member.Class.ToString(),Member.Type.ToString(),Member.Rows,Member.Columns,Member.Elements));
                        }
                    }
                }
                Buffers.Add(new(Name(Description.Name),Description.Size,Variables));
            }
            return Buffers;
        } finally {
            if(Reflection!=null){ _=Reflection->Release(); }
            if(Utils!=null){ _=Utils->Release(); }
        }
    }
    private static unsafe string Name(sbyte*Value)=>Marshal.PtrToStringUTF8((nint)Value)??string.Empty;
    private static void Check(HRESULT Result,string Operation){
        if(Result.FAILED){ throw new InvalidDataException($"{Operation} failed: 0x{Result.Value:X8}."); }
    }
}
