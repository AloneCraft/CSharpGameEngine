namespace CSharpGameEngine.FontBaker;

/// <summary>Turns a glyph's high-resolution coverage into the cell of 8-bit distances that goes into the atlas.</summary>
internal static class GlyphField {
    /// <summary>
    /// <paramref name="Inside"/> is the glyph at <paramref name="Supersample"/> times the atlas resolution (width and height are multiples of it).
    /// Each returned byte is the signed distance to the outline, averaged over its block of the mask, in atlas pixels mapped so that the outline is
    /// 128, <paramref name="SpreadPixels"/> inside is 255 and the same distance outside is 0.
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

    /// <summary>0.5 on the outline, 1 at <paramref name="SpreadPixels"/> inside and 0 at the same distance outside, as a byte.</summary>
    internal static byte Encode(float SignedDistancePixels,int SpreadPixels){
        float Value=0.5f+(SignedDistancePixels/(2f*SpreadPixels));
        return(byte)MathF.Round(Math.Clamp(Value,0f,1f)*255f);
    }
}

/// <summary>Packs rectangles into rows of an atlas of a given width; the rows are as high as their tallest rectangle.</summary>
internal static class ShelfPacker {
    /// <summary>The top-left position of every rectangle (in the order given) and the height the atlas needs. Throws if a rectangle is wider than the atlas.</summary>
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
