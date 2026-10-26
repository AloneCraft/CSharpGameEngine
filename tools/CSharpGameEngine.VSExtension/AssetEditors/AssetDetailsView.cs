using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットエディターの Details 欄。Assets ウィンドウの Inspector（<see cref="AssetInspectorControl"/>）を、グローバルな選択に追従させず
/// このエディターのアセットだけを表示する形で再利用する。型付きデータの編集、FBX／画像の取り込み設定、再取り込み、メタデータ登録は
/// Inspector と同じ経路（ライブバッファ・ネイティブ Undo・Content サービス）を通るので、編集の正本は 1 つだけである。
/// アセットがゲームプロジェクトの在庫にない場合は、その旨を表示し、在庫の更新時に再試行する。
/// </summary>
internal sealed class AssetDetailsView:Grid,IDisposable {
    private string SourcePath;
    private readonly AssetInspectorControl Inspector=new(FollowSelection:false);
    private readonly TextBlock Message=new(){ Margin=new Thickness(8),TextWrapping=TextWrapping.Wrap };
    private AssetWorkspaceController?Controller;
    private bool Disposed;
    internal AssetDetailsView(string SourcePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SourcePath=SourcePath;
        AssetTheme.Apply(this);
        this.SetResourceReference(Panel.BackgroundProperty,VsBrushes.WindowKey);
        this.Message.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.Children.Add(this.Inspector);
        this.Children.Add(this.Message);
        this.Controller=AssetsToolWindowPane.Controller;
        if(this.Controller is not null){ this.Controller.Changed+=this.Refresh; }
        this.Refresh();
    }
    /// <summary>このエディターのアセットに対応する在庫項目（まだ在庫にない場合は null）。</summary>
    internal AssetWorkspaceItem?Item=>this.Controller?.Items.FirstOrDefault(Candidate=>string.Equals(Candidate.Source.SourcePath,this.SourcePath,StringComparison.OrdinalIgnoreCase));
    internal void Refresh(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ return; }
        var Current=this.Item;
        if(Current is null){
            this.Inspector.Visibility=Visibility.Collapsed;
            this.Message.Visibility=Visibility.Visible;
            this.Message.Text=this.Controller is null?VsixText.Get("AssetEditor_DetailsUnavailable"):VsixText.Get("AssetEditor_NotInInventory");
            return;
        }
        this.Message.Visibility=Visibility.Collapsed;
        this.Inspector.Visibility=Visibility.Visible;
        this.Inspector.Show([Current]);
    }
    /// <summary>VS が DocData を別のパスへ向けた（名前変更・再読み込み）。</summary>
    internal void Retarget(string SourcePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.SourcePath=SourcePath;
        this.Refresh();
    }
    /// <summary>Ctrl+S／Z／Y（型付きデータの保存と Undo）。</summary>
    internal bool HandleNativeShortcut(System.Windows.Input.Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.Inspector.HandleNativeShortcut(Key);
    }
    public void Dispose(){
        this.Disposed=true;
        if(this.Controller is not null){ this.Controller.Changed-=this.Refresh; }
        this.Controller=null;
        this.Inspector.Dispose();
    }
}
