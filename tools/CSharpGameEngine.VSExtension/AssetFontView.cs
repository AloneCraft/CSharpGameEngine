using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// フォントの見本・対応する言語・表示できない文字の表示（ツールチップと Inspector で共有）。内容は <see cref="AssetFontData"/> と
/// <see cref="AssetFontCoverage"/> が作り、ここは並べるだけ。
/// </summary>
internal static class AssetFontView {
    /// <summary>ツールチップ用の内容を、読み込み前は「読み込み中」、開いたときに 1 回だけ読んで埋める。タイルの一覧を作る時点では何も読まない。</summary>
    internal static ToolTip CreateTooltip(string Path){
        var Panel=new StackPanel { MaxWidth=460 };
        Panel.Children.Add(new TextBlock { Text=VsixText.Get("Font_TipLoading") });
        var Tip=new ToolTip { Content=Panel,Padding=new Thickness(8) };
        var Started=false;
        var Loader=new TooltipLoader();
        Tip.Opened+=(_,_)=>{
            if(Started){ return; }
            Started=true;
            Loader.Loading=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
                AssetFontData?Data=null;
                try { Data=await System.Threading.Tasks.Task.Run(()=>AssetFontData.LoadAsync(Path,CancellationToken.None)); }
                catch(Exception Error)when(Error is System.IO.IOException or InvalidOperationException or System.IO.InvalidDataException or UnauthorizedAccessException){ Data=null; }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Panel.Children.Clear();
                if(Data is null){ Panel.Children.Add(new TextBlock { Text=VsixText.Get("Font_TipUnavailable"),TextWrapping=TextWrapping.Wrap });return; }
                Populate(Panel,Data,Detailed:false,Ink(Tip.Foreground));
            });
        };
        return Tip;
    }
    /// <summary>ツールチップごとの読み込みの JoinableTask を持つ（呼び出し側が待たないので、フィールドに保持する）。</summary>
    private sealed class TooltipLoader { internal Microsoft.VisualStudio.Threading.JoinableTask?Loading; }
    internal static Brush Ink(Brush?Preferred)=>Preferred??(Application.Current?.TryFindResource(VsBrushes.WindowTextKey) as Brush)??Brushes.White;
    /// <summary>
    /// <paramref name="Panel"/> に内容を並べる。<paramref name="Detailed"/> が false（ツールチップ）は、題名・例文・対応する言語・表示できない記号の要約。
    /// true（Inspector）は、それに加えて記号の行、言語ごとの状態、字数・アトラス・指標、実行時の動きの説明を出す。
    /// </summary>
    internal static void Populate(StackPanel Panel,AssetFontData Data,bool Detailed,Brush Foreground){
        var Culture=VsixLocalization.UICulture;
        var Report=Data.Report;
        Panel.Children.Add(new TextBlock { Text=VsixText.Format("Font_TipTitle",Data.Name,Data.GlyphCount.ToString("N0",Culture)),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
        AddSample(Panel,Data,Report.Sample.Text,22f,Foreground);
        if(Report.Sample.MissingIndexes.Count>0){
            Panel.Children.Add(Note(VsixText.Format("Font_MissingInSample",Report.Sample.MissingIndexes.Count.ToString(Culture))));
        }
        if(Detailed){
            Panel.Children.Add(new TextBlock { Text=VsixText.Get("Font_Sample"),Margin=new Thickness(0,8,0,0),FontWeight=FontWeights.SemiBold });
            AddSample(Panel,Data,"Aa Bb Cc 0123456789",28f,Foreground);
            AddSample(Panel,Data,AssetFontCoverage.Symbols,22f,Foreground);
        }
        AddLanguages(Panel,Report,Detailed);
        var Missing=Report.MissingSymbols;
        Panel.Children.Add(Missing.Count==0
            ?Note(VsixText.Get("Font_NoMissingSymbols"))
            :Note(VsixText.Format("Font_MissingSymbols",Missing.Count.ToString(Culture),Report.SymbolTotal.ToString(Culture),string.Concat(Missing.Take(Detailed?Missing.Count:16)))));
        if(Detailed){
            Panel.Children.Add(Note(VsixText.Get("Font_FallbackNote")));
            if(Data.Baked is{ } Baked){
                Panel.Children.Add(Note(VsixText.Format("Font_Metrics",Data.GlyphCount.ToString("N0",Culture),Data.AtlasWidth.ToString(Culture),Data.AtlasHeight.ToString(Culture),
                    Baked.Ascent.ToString("0.###",Culture),Baked.Descent.ToString("0.###",Culture))));
            }
        }
    }
    private static TextBlock Note(string Text)=>new(){ Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0),Opacity=0.85f };
    private static void AddSample(StackPanel Panel,AssetFontData Data,string Text,float Size,Brush Foreground){
        if(Data.CreateSample(Text,Size,Foreground) is{ } Element){ Element.Margin=new Thickness(0,4,0,0);Panel.Children.Add(Element); }
    }
    private static void AddLanguages(StackPanel Panel,FontCoverageReport Report,bool Detailed){
        var Culture=VsixLocalization.UICulture;
        string Name(FontLanguageResult Result)=>VsixText.Get("FontLang_"+Result.Id);
        if(Detailed){
            Panel.Children.Add(new TextBlock { Text=VsixText.Get("Font_Languages"),Margin=new Thickness(0,8,0,0),FontWeight=FontWeights.SemiBold });
            foreach(var Result in Report.Languages){
                var Status=Result.Support switch {
                    FontLanguageSupport.Full=>VsixText.Get("Font_LangFull"),
                    FontLanguageSupport.Partial=>VsixText.Format("Font_LangPartial",Result.Supported.ToString(Culture),Result.Total.ToString(Culture)),
                    _=>VsixText.Get("Font_LangNone"),
                };
                var Row=new Grid { Margin=new Thickness(0,1,0,0) };
                Row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(150) });
                Row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                var Label=new TextBlock { Text=Name(Result) };
                var State=new TextBlock { Text=Status,Opacity=Result.Support==FontLanguageSupport.None?0.55f:1f };
                Grid.SetColumn(State,1);
                Row.Children.Add(Label);Row.Children.Add(State);
                Panel.Children.Add(Row);
            }
            return;
        }
        var Full=string.Join(", ",Report.Languages.Where(static Result=>Result.Support==FontLanguageSupport.Full).Select(Name));
        var Partial=string.Join(", ",Report.Languages.Where(static Result=>Result.Support==FontLanguageSupport.Partial).Select(Result=>Name(Result)+" ("+Result.Supported.ToString(Culture)+"/"+Result.Total.ToString(Culture)+")"));
        if(Full.Length>0){ Panel.Children.Add(Note(VsixText.Format("Font_TipLanguages",Full))); }
        if(Partial.Length>0){ Panel.Children.Add(Note(VsixText.Format("Font_TipPartial",Partial))); }
    }
}
