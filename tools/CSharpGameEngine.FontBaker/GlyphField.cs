namespace CSharpGameEngine.FontBaker;

/// <summary>グリフの高解像度カバレッジを、アトラスに格納する 8 ビット距離のセルへ変換する。</summary>
internal static class GlyphField {
    /// <summary>
    /// <paramref name="Inside"/> はアトラス解像度の <paramref name="Supersample"/> 倍で描画したグリフ（幅と高さはその倍数）。返される各 byte は、マスク内のブロックで平均した輪郭までの符号付き距離をアトラス上のピクセル単位で表す。輪郭は 128、<paramref name="SpreadPixels"/> 分だけ内側は 255、同じ距離だけ外側は 0 となる。
    /// </summary>
    internal static byte[]ToCell(bool[]Inside,int Width,int Height,int Supersample,int SpreadPixels,out int CellWidth,out int CellHeight){
        if(Supersample<1||Width%Supersample!=0||Height%Supersample!=0){ throw new ArgumentException("The mask must be a multiple of the supersampling factor."); }
        if(SpreadPixels<1){ throw new ArgumentOutOfRangeException(nameof(SpreadPixels)); }
        float[]Distance=DistanceTransform.Signed(Inside,Width,Height);
        CellWidth=Width/Supersample;
        CellHeight=Height/Supersample;
        byte[]Cell=new byte[CellWidth*CellHeight];
        float BlockArea=Supersample*Supersample;
        for(int Y=0;Y<CellHeight;Y++){
            for(int X=0;X<CellWidth;X++){
                float Sum=0f;
                for(int SampleY=0;SampleY<Supersample;SampleY++){
                    for(int SampleX=0;SampleX<Supersample;SampleX++){
                        Sum+=Distance[(((Y*Supersample)+SampleY)*Width)+(X*Supersample)+SampleX];
                    }
                }
                float AtlasPixels=Sum/BlockArea/Supersample;
                Cell[(Y*CellWidth)+X]=Encode(AtlasPixels,SpreadPixels);
            }
        }
        return Cell;
    }

    /// <summary>輪郭上で 0.5、内側の <paramref name="SpreadPixels"/> の位置で 1、同じ距離の外側で 0 を表す byte 値。</summary>
    internal static byte Encode(float SignedDistancePixels,int SpreadPixels){
        float Value=0.5f+(SignedDistancePixels/(2f*SpreadPixels));
        return(byte)MathF.Round(Math.Clamp(Value,0f,1f)*255f);
    }
}

/// <summary>指定した幅のアトラスに矩形を行単位で詰める。行の高さは、その行で最も高い矩形の高さとなる。</summary>
internal static class ShelfPacker {
    /// <summary>各矩形の左上位置（指定順）とアトラスに必要な高さ。矩形の幅がアトラスを超えると例外をスローする。</summary>
    internal static(int X,int Y)[]Pack(IReadOnlyList<(int Width,int Height)>Sizes,int AtlasWidth,int Gap,out int AtlasHeight){
        (int X,int Y)[]Positions=new(int X,int Y)[Sizes.Count];
        int[]Order=[.. Enumerable.Range(0,Sizes.Count).OrderByDescending(Index=>Sizes[Index].Height).ThenBy(Index=>Index)];
        int PenX=0;
        int PenY=0;
        int RowHeight=0;
        foreach(int Index in Order){
            (int Width,int Height)=Sizes[Index];
            if(Width>AtlasWidth){ throw new InvalidOperationException($"A glyph of {Width} pixels does not fit an atlas {AtlasWidth} pixels wide."); }
            if(PenX+Width>AtlasWidth){
                PenX=0;
                PenY+=RowHeight+Gap;
                RowHeight=0;
            }
            Positions[Index]=(PenX,PenY);
            PenX+=Width+Gap;
            RowHeight=Math.Max(RowHeight,Height);
        }
        AtlasHeight=Math.Max(1,PenY+RowHeight);
        return Positions;
    }
}
