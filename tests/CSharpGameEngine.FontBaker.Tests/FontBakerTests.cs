using CSharpGameEngine.FontBaker;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.FontBaker.Tests;

/// <summary>ADR-0078/M0104: the font baker's distance transform, packing and encoding, and one bake of a Windows font.</summary>
public sealed class FontBakerTests {
    private static float[]BruteForce(bool[]Set,int Width,int Height){
        float[]Result=new float[Set.Length];
        for(int Y=0;Y<Height;Y++){
            for(int X=0;X<Width;X++){
                float Best=float.PositiveInfinity;
                for(int OtherY=0;OtherY<Height;OtherY++){
                    for(int OtherX=0;OtherX<Width;OtherX++){
                        if(!Set[(OtherY*Width)+OtherX]){ continue; }
                        float DX=X-OtherX;
                        float DY=Y-OtherY;
                        Best=MathF.Min(Best,(DX*DX)+(DY*DY));
                    }
                }
                Result[(Y*Width)+X]=Best;
            }
        }
        return Result;
    }

    [Theory]
    [InlineData(1,1,1)]
    [InlineData(7,5,2)]
    [InlineData(16,16,3)]
    [InlineData(23,9,4)]
    [InlineData(40,31,5)]
    public void SquaredDistanceToSet_MatchesABruteForceSearch(int Width,int Height,int Seed){
        Random Random=new(Seed);
        bool[]Set=new bool[Width*Height];
        for(int Index=0;Index<Set.Length;Index++){ Set[Index]=Random.Next(0,12)==0; }
        Set[Random.Next(Set.Length)]=true;

        float[]Fast=DistanceTransform.SquaredDistanceToSet(Set,Width,Height);
        float[]Slow=BruteForce(Set,Width,Height);

        for(int Index=0;Index<Fast.Length;Index++){
            Assert.True(Math.Abs(Fast[Index]-Slow[Index])<0.01f,$"pixel {Index}: {Fast[Index]} instead of {Slow[Index]}");
        }
    }

    [Fact]
    public void SquaredDistanceToSet_OfAnEmptySetIsFarEverywhere(){
        float[]Distance=DistanceTransform.SquaredDistanceToSet(new bool[12],4,3);

        Assert.All(Distance,static Value=>Assert.True(Value>1e10f));
    }

    [Fact]
    public void Signed_IsPositiveInsideNegativeOutsideAndAHalfPixelAtTheOutline(){
        // An 8 x 4 image whose left half (4 columns) is inside.
        bool[]Inside=new bool[8*4];
        for(int Y=0;Y<4;Y++){ for(int X=0;X<4;X++){ Inside[(Y*8)+X]=true; } }

        float[]Signed=DistanceTransform.Signed(Inside,8,4);

        Assert.Equal(0.5f,Signed[3],3);
        Assert.Equal(-0.5f,Signed[4],3);
        Assert.Equal(1.5f,Signed[2],3);
        Assert.Equal(-2.5f,Signed[6],3);
        Assert.All(Enumerable.Range(0,4),Column=>Assert.True(Signed[Column]>0f));
        Assert.All(Enumerable.Range(4,4),Column=>Assert.True(Signed[Column]<0f));
    }

    [Theory]
    [InlineData(0f,8,128)]
    [InlineData(8f,8,255)]
    [InlineData(-8f,8,0)]
    [InlineData(100f,8,255)]
    [InlineData(-100f,8,0)]
    [InlineData(4f,8,191)]
    public void Encode_PutsTheOutlineAtHalfAndTheSpreadAtTheEnds(float Distance,int Spread,int Expected){
        Assert.Equal(Expected,GlyphField.Encode(Distance,Spread));
    }

    [Fact]
    public void ToCell_ADiscIsHighInsideLowOutsideAndHalfAtItsEdge(){
        // A disc of radius 24 in a 64 x 64 mask, brought down by 4 to a 16 x 16 cell with a spread of 4 cell pixels (6 pixels radius).
        bool[]Inside=new bool[64*64];
        for(int Y=0;Y<64;Y++){
            for(int X=0;X<64;X++){
                float DX=X+0.5f-32f;
                float DY=Y+0.5f-32f;
                Inside[(Y*64)+X]=(DX*DX)+(DY*DY)<=24f*24f;
            }
        }

        byte[]Cell=GlyphField.ToCell(Inside,64,64,4,4,out int Width,out int Height);

        Assert.Equal(16,Width);
        Assert.Equal(16,Height);
        Assert.True(Cell[(8*16)+8]>=250,"the centre is deep inside");
        Assert.True(Cell[0]<=5,"the corner is far outside");
        // The disc's edge is 6 cell pixels from the centre (cell coordinate 8), between pixels 13 and 14. Pixel 13's centre (13.5) is half a pixel
        // inside: 0.5 + 0.5/8 of the range, about 143. Pixel 14's centre is half a pixel outside: about 112.
        Assert.InRange(Cell[(8*16)+13],138,148);
        Assert.InRange(Cell[(8*16)+14],107,117);
        // Symmetric about the centre: pixel k mirrors pixel 15 - k.
        Assert.InRange(Math.Abs(Cell[(8*16)+2]-Cell[(8*16)+13]),0,6);
        Assert.InRange(Math.Abs(Cell[(8*16)+1]-Cell[(8*16)+14]),0,6);
    }

    [Fact]
    public void ShelfPacker_PlacesEveryRectangleInsideTheAtlasWithoutOverlap_AndIsDeterministic(){
        Random Random=new(7);
        (int Width,int Height)[]Sizes=[.. Enumerable.Range(0,150).Select(_=>(Random.Next(3,60),Random.Next(3,60)))];

        var First=ShelfPacker.Pack(Sizes,512,1,out int Height);
        var Second=ShelfPacker.Pack(Sizes,512,1,out int SecondHeight);

        Assert.Equal(First,Second);
        Assert.Equal(Height,SecondHeight);
        for(int Index=0;Index<Sizes.Length;Index++){
            Assert.InRange(First[Index].X,0,512-Sizes[Index].Width);
            Assert.InRange(First[Index].Y,0,Height-Sizes[Index].Height);
            for(int Other=Index+1;Other<Sizes.Length;Other++){
                bool Apart=First[Index].X+Sizes[Index].Width<=First[Other].X||First[Other].X+Sizes[Other].Width<=First[Index].X
                    ||First[Index].Y+Sizes[Index].Height<=First[Other].Y||First[Other].Y+Sizes[Other].Height<=First[Index].Y;
                Assert.True(Apart,$"rectangles {Index} and {Other} overlap");
            }
        }
    }

    [Fact]
    public void ShelfPacker_RejectsARectangleWiderThanTheAtlas(){
        Assert.Throws<InvalidOperationException>(()=>ShelfPacker.Pack([(600,10)],512,1,out _));
    }

    [Fact]
    public void Bake_OfAWindowsFont_GivesAUsableAtlasThatSurvivesTheFileFormat(){
        string Path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Fonts","arial.ttf");
        if(!File.Exists(Path)){ return; }

        FontAtlas Font=FontBaking.Bake(Path,[' ','A','I','l','.'],48,6,4);
        FontAtlas Read=FontAtlas.Parse(Font.ToBytes());

        Assert.Equal(5,Read.Glyphs.Count);
        Assert.InRange(Read.Ascent,0.7f,1.1f);
        Assert.InRange(Read.Descent,0.1f,0.4f);
        Assert.Equal(6f/48f,Read.SpreadEm,4);
        Assert.True(Read.TryGetGlyph(' ',out FontGlyph Space));
        Assert.False(Space.HasQuad);
        Assert.InRange(Space.Advance,0.2f,0.35f);
        Assert.True(Read.TryGetGlyph('I',out FontGlyph I));
        Assert.True(I.HasQuad);
        // The vertical bar's stem is inside the glyph: the centre of its cell is above the outline value; the cell's corner is outside it.
        int Left=(int)MathF.Round(I.U0*Read.AtlasWidth);
        int Top=(int)MathF.Round(I.V0*Read.AtlasHeight);
        int Right=(int)MathF.Round(I.U1*Read.AtlasWidth);
        int Bottom=(int)MathF.Round(I.V1*Read.AtlasHeight);
        Assert.True(Read.Distance[(((Top+Bottom)/2)*Read.AtlasWidth)+((Left+Right)/2)]>140,"the stem's centre is inside");
        Assert.True(Read.Distance[(Top*Read.AtlasWidth)+Left]<100,"the cell's corner is outside");
        // The glyph's quad starts below the cap height above the baseline and ends on or below it.
        Assert.InRange(I.PlaneTop,0.6f,1.0f);
        Assert.True(I.PlaneBottom<=0f);
        Assert.True(Read.TryGetGlyph('A',out FontGlyph A));
        Assert.True(A.Advance>I.Advance);
    }
}
