using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>Inspector の「情報」の節の 1 行（ラベルと値、どちらも表示する文字列）。</summary>
internal sealed record AssetFact(string Label,string Value);
/// <summary>
/// アセットの読み取り専用の情報（UE や Unity のインスペクターの「情報」の節）。ファイルの大きさ・更新日時・提供元は全種類で、
/// モデルは頂点・三角形・大きさ・スキン・クリップ、テクスチャは画素の大きさ、音声は形式と長さを足す。組み立ては純粋な関数にして単体テストする。
/// </summary>
internal static class AssetInspectorFacts {
    internal static string FormatBytes(long Bytes,CultureInfo Culture){
        if(Bytes<1024){ return Bytes.ToString("N0",Culture)+" B"; }
        var Value=Bytes/1024f;var Unit="KB";
        if(Value>=1024f){ Value/=1024f;Unit="MB"; }
        if(Value>=1024f){ Value/=1024f;Unit="GB"; }
        return Value.ToString(Value>=100f?"N0":"N1",Culture)+" "+Unit;
    }
    internal static string FormatDuration(float Seconds,CultureInfo Culture){
        var Total=Seconds<0f?0f:Seconds;
        var Minutes=(int)(Total/60f);
        return Minutes.ToString(Culture)+":"+(Total-Minutes*60f).ToString("00.000",Culture);
    }
    /// <summary>全種類の共通の情報。ファイルが無い（登録だけ残っている）ときは空。</summary>
    internal static IReadOnlyList<AssetFact>Common(AssetWorkspaceItem Item,CultureInfo Culture,string ProviderLabel){
        var Facts=new List<AssetFact>();
        if(Item.SourceLength>0){ Facts.Add(new(VsixText.Get("AssetInfo_FileSize",Culture),FormatBytes(Item.SourceLength,Culture))); }
        if(Item.ModifiedUtcTicks>0){
            Facts.Add(new(VsixText.Get("AssetInfo_Modified",Culture),new DateTime(Item.ModifiedUtcTicks,DateTimeKind.Utc).ToLocalTime().ToString("g",Culture)));
        }
        Facts.Add(new(VsixText.Get("AssetInfo_Provider",Culture),ProviderLabel));
        return Facts;
    }
    internal static IReadOnlyList<AssetFact>Model(AssetMeshStatistics?Stats,GltfSkeletalInfo?Probe,CultureInfo Culture){
        var Facts=new List<AssetFact>();
        if(Stats is not null){
            Facts.Add(new(VsixText.Get("AssetInfo_Vertices",Culture),Stats.VertexCount.ToString("N0",Culture)));
            Facts.Add(new(VsixText.Get("AssetInfo_Triangles",Culture),Stats.TriangleCount.ToString("N0",Culture)));
            var Size=Stats.Size;
            Facts.Add(new(VsixText.Get("AssetInfo_Size",Culture),string.Join(" × ",Size.X.ToString("0.###",Culture),Size.Y.ToString("0.###",Culture),Size.Z.ToString("0.###",Culture))));
        }
        if(Probe is not null){
            Facts.Add(new(VsixText.Get("AssetInfo_Skins",Culture),Probe.SkinCount.ToString(Culture)));
            Facts.Add(new(VsixText.Get("AssetInfo_Clips",Culture),Probe.AnimationNames.Count.ToString(Culture)));
        }
        return Facts;
    }
    internal static IReadOnlyList<AssetFact>Texture(int Width,int Height,CultureInfo Culture)=>
        Width>0&&Height>0?[new(VsixText.Get("AssetInfo_Dimensions",Culture),Width.ToString("N0",Culture)+" × "+Height.ToString("N0",Culture)+" px")]:[];
    internal static IReadOnlyList<AssetFact>Audio(AssetAudioPreview Preview,CultureInfo Culture)=>[
        new(VsixText.Get("AssetInfo_SampleRate",Culture),Preview.SampleRate.ToString("N0",Culture)+" Hz"),
        new(VsixText.Get("AssetInfo_Channels",Culture),Preview.Channels==1?VsixText.Get("AssetInfo_Mono",Culture):VsixText.Get("AssetInfo_Stereo",Culture)),
        new(VsixText.Get("AssetInfo_BitDepth",Culture),Preview.BitsPerSample.ToString(Culture)+" bit"),
        new(VsixText.Get("AssetInfo_Duration",Culture),FormatDuration(Preview.DurationSeconds,Culture)),
    ];
    /// <summary>モデルの統計を読む上限（バイト）。これを超えるファイルは頂点数などを出さない。</summary>
    internal const long MaximumMeshBytes=64L*1024*1024;
}
internal sealed partial class AssetInspectorControl {
    /// <summary>「情報」の節。共通の行はすぐ、種類ごとの行は読み終えてから足す（選択が変わったら捨てる）。</summary>
    private void ShowFacts(AssetWorkspaceItem Item,string ProviderLabel,int Generation){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Culture=VsixLocalization.UICulture;
        var Rows=new StackPanel();
        void AddRows(IEnumerable<AssetFact>Facts){
            foreach(var Fact in Facts){
                var Row=new Grid { Margin=new Thickness(0,1,0,1) };
                Row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(130) });
                Row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                var Label=new TextBlock { Text=Fact.Label,Opacity=0.75f,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=Fact.Label };
                var Value=new TextBlock { Text=Fact.Value,TextWrapping=TextWrapping.Wrap,ToolTip=Fact.Value };
                Grid.SetColumn(Value,1);
                Row.Children.Add(Label);Row.Children.Add(Value);
                Rows.Children.Add(Row);
            }
        }
        AddRows(AssetInspectorFacts.Common(Item,Culture,ProviderLabel));
        var Section=new Expander { Header=VsixText.Get("AssetInfo_Section"),IsExpanded=true,Content=Rows,Margin=new Thickness(0,8,0,0) };
        Section.SetResourceReference(Control.ForegroundProperty,VsBrushes.WindowTextKey);
        this.Panel.Children.Add(Section);
        var Path=Item.Source.SourcePath;
        var Extension=System.IO.Path.GetExtension(Path).ToLowerInvariant();
        if(Item.Kind==AssetWorkspaceKind.Texture||Item.Kind==AssetWorkspaceKind.Image){
            if(AssetImageHeader.TryReadFile(Path,out var Width,out var Height)){ AddRows(AssetInspectorFacts.Texture(Width,Height,Culture)); }
        } else if(Item.Kind==AssetWorkspaceKind.Model&&Extension is ".glb" or ".gltf" or ".fbx"){
            _=this.LoadModelFactsAsync(Item,Extension,Generation,AddRows);
        } else if(Item.Kind==AssetWorkspaceKind.Audio&&Extension==".wav"){
            _=this.LoadAudioFactsAsync(Item,Generation,AddRows);
        }
    }
    private async Task LoadModelFactsAsync(AssetWorkspaceItem Item,string Extension,int Generation,Action<IEnumerable<AssetFact>>AddRows){
        AssetMeshStatistics?Stats=null;GltfSkeletalInfo?Probe=null;
        try {
            var Path=Item.Source.SourcePath;
            (Stats,Probe)=await Task.Run(()=>{
                var Info=Extension==".fbx"?null:GltfSkeletalProbe.Probe(Path);
                if(new FileInfo(Path).Length>AssetInspectorFacts.MaximumMeshBytes){ return ((AssetMeshStatistics?)null,Info); }
                var Context=CgeProjectContext.Resolve(Path);
                if(Context is null){ return ((AssetMeshStatistics?)null,Info); }
                var Mesh=Extension==".fbx"?ExternalFbxLoader.Load(Path,Context.WorkspaceRoot):ExternalGltfLoader.Load(Path,Context.WorkspaceRoot);
                return (AssetMeshStatistics.From(Mesh),Info);
            });
        } catch(Exception Error)when(Error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException){
            // 読めない・大きすぎるモデルは、ファイルの情報だけ出す。
        }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(this.Disposed||Generation!=this.Generation){ return; }
        AddRows(AssetInspectorFacts.Model(Stats,Probe,VsixLocalization.UICulture));
    }
    private async Task LoadAudioFactsAsync(AssetWorkspaceItem Item,int Generation,Action<IEnumerable<AssetFact>>AddRows){
        AssetAudioPreview?Preview=null;
        try {
            var Context=CgeProjectContext.Resolve(Item.Source.SourcePath);
            if(Context is not null){
                var Request=new AssetWorkspaceRequest { WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new AssetWorkspaceSource { SourcePath=Item.Source.SourcePath }] };
                Preview=(await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(Request,Context.WorkspaceRoot))).AudioPreview;
            }
        } catch(Exception Error)when(Error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException){
            Preview=null;
        }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(this.Disposed||Generation!=this.Generation||Preview is null){ return; }
        AddRows(AssetInspectorFacts.Audio(Preview,VsixLocalization.UICulture));
    }
}
