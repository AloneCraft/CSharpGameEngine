namespace CSharpGameEngine.Rendering.Streaming;
/// <summary>
/// 遮蔽判定に使う Hi-Z の段の決まり（ADR-0100 決定 3、M0131 単位 1）。主パスが書いた物理距離の画像を、2×2 画素ごとにいちばん遠い値を取って半分ずつ縮める。
/// 段 0 の大きさは、画面の半分（切り上げ）を縦横それぞれ 2 のべき乗へ切り上げたもの。D3D12 のミップは半分の切り捨てなので、2 のべき乗にしておくと
/// どの段もちょうど半分になり、段 n の 1 画素が段 n-1 の 2×2（1 画素の向きは 1×2・2×1）を過不足なく覆う。段 0 の画素 (x, y) は画面の 2x〜2x+1、2y〜2y+1 を覆う。
/// 距離 0（何も描かれていない空）は無限に遠いとする。画面の外の画素は 0 とし、いちばん遠い値には効かない（画面の外は何も分からないので、そこだけにある物は描かない側に倒れるが、画面の外の物はもともと写らない）。
/// GPU 側は assets/Shaders/HiZ.hlsl で、<see cref="Build"/> はその答え合わせに使う。
/// </summary>
public static class HiZPyramid {
    /// <summary>画面の大きさから、段ごとの大きさ（段 0 から 1×1 まで）。</summary>
    public static (int Width,int Height)[]LevelSizes(int ScreenWidth,int ScreenHeight){
        ArgumentOutOfRangeException.ThrowIfLessThan(ScreenWidth,1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ScreenHeight,1);
        List<(int Width,int Height)>Sizes=[];
        var Width=(int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)((ScreenWidth+1)/2));
        var Height=(int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)((ScreenHeight+1)/2));
        Sizes.Add((Width,Height));
        while(Width>1||Height>1){
            Width=Math.Max(Width/2,1);
            Height=Math.Max(Height/2,1);
            Sizes.Add((Width,Height));
        }
        return [..Sizes];
    }
    /// <summary>CPU で全段を作る（行優先、段ごとの配列）。<paramref name="Distances"/> は画面の大きさの距離の画像（行優先）。</summary>
    public static float[][]Build(ReadOnlySpan<float>Distances,int ScreenWidth,int ScreenHeight){
        if(Distances.Length!=ScreenWidth*ScreenHeight){ throw new ArgumentException("The distance image does not match the screen size.",nameof(Distances)); }
        var Sizes=LevelSizes(ScreenWidth,ScreenHeight);
        var Levels=new float[Sizes.Length][];
        var Source=new float[Distances.Length];
        for(var Index=0;Index<Source.Length;Index++){ Source[Index]=Distances[Index]>0f?Distances[Index]:float.PositiveInfinity; }
        var SourceWidth=ScreenWidth;
        var SourceHeight=ScreenHeight;
        for(var Level=0;Level<Sizes.Length;Level++){
            var (Width,Height)=Sizes[Level];
            var Destination=new float[Width*Height];
            for(var Y=0;Y<Height;Y++){
                for(var X=0;X<Width;X++){
                    var Farthest=0f;
                    for(var Dy=0;Dy<2;Dy++){
                        for(var Dx=0;Dx<2;Dx++){
                            int Sx=X*2+Dx,Sy=Y*2+Dy;
                            if(Sx<SourceWidth&&Sy<SourceHeight){ Farthest=MathF.Max(Farthest,Source[Sy*SourceWidth+Sx]); }
                        }
                    }
                    Destination[Y*Width+X]=Farthest;
                }
            }
            Levels[Level]=Destination;
            Source=Destination;
            SourceWidth=Width;
            SourceHeight=Height;
        }
        return Levels;
    }
}
