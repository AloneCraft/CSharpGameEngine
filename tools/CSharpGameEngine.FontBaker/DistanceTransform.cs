namespace CSharpGameEngine.FontBaker;

/// <summary>
/// The exact Euclidean distance transform (Felzenszwalb and Huttenlocher, "Distance Transforms of Sampled Functions"): two linear passes over
/// the rows and columns, and the signed distance field built from it.
/// </summary>
internal static class DistanceTransform {
    private const float Infinity=1e20f;

    /// <summary>The squared distance, in pixels, from every pixel to the nearest pixel that is set in <paramref name="Set"/> (zero on the set itself).</summary>
    internal static float[]SquaredDistanceToSet(bool[]Set,int Width,int Height){
        if(Set.Length!=Width*Height){ throw new ArgumentException("The mask must be width × height.",nameof(Set)); }
        float[]Grid=new float[Set.Length];
        for(int Index=0;Index<Grid.Length;Index++){ Grid[Index]=Set[Index]?0f:Infinity; }
        int Longest=Math.Max(Width,Height);
        float[]Column=new float[Longest];
        float[]Output=new float[Longest];
        int[]Parabolas=new int[Longest];
        float[]Boundaries=new float[Longest+1];
        for(int X=0;X<Width;X++){
            for(int Y=0;Y<Height;Y++){ Column[Y]=Grid[(Y*Width)+X]; }
            Transform(Column,Output,Parabolas,Boundaries,Height);
            for(int Y=0;Y<Height;Y++){ Grid[(Y*Width)+X]=Output[Y]; }
        }
        for(int Y=0;Y<Height;Y++){
            for(int X=0;X<Width;X++){ Column[X]=Grid[(Y*Width)+X]; }
            Transform(Column,Output,Parabolas,Boundaries,Width);
            for(int X=0;X<Width;X++){ Grid[(Y*Width)+X]=Output[X]; }
        }
        return Grid;
    }

    /// <summary>
    /// The signed distance, in pixels, to the outline between the inside and the outside pixels: positive inside, negative outside, and
    /// ±0.5 on the pixels next to the outline (the outline lies halfway between an inside and an outside pixel).
    /// </summary>
    internal static float[]Signed(bool[]Inside,int Width,int Height){
        bool[]Outside=new bool[Inside.Length];
        for(int Index=0;Index<Inside.Length;Index++){ Outside[Index]=!Inside[Index]; }
        float[]ToInside=SquaredDistanceToSet(Inside,Width,Height);
        float[]ToOutside=SquaredDistanceToSet(Outside,Width,Height);
        float[]Result=new float[Inside.Length];
        for(int Index=0;Index<Result.Length;Index++){
            Result[Index]=Inside[Index]
                ?MathF.Min(MathF.Sqrt(ToOutside[Index]),1e6f)-0.5f
                :-(MathF.Min(MathF.Sqrt(ToInside[Index]),1e6f)-0.5f);
        }
        return Result;
    }

    /// <summary>The lower envelope of the parabolas rooted at each sample, evaluated at each position.</summary>
    private static void Transform(float[]Samples,float[]Output,int[]Parabolas,float[]Boundaries,int Count){
        int Last=0;
        Parabolas[0]=0;
        Boundaries[0]=-Infinity;
        Boundaries[1]=Infinity;
        for(int Position=1;Position<Count;Position++){
            float Crossing=Intersection(Samples,Position,Parabolas[Last]);
            while(Last>0&&Crossing<=Boundaries[Last]){
                Last--;
                Crossing=Intersection(Samples,Position,Parabolas[Last]);
            }
            Last++;
            Parabolas[Last]=Position;
            Boundaries[Last]=Crossing;
            Boundaries[Last+1]=Infinity;
        }
        int Current=0;
        for(int Position=0;Position<Count;Position++){
            while(Boundaries[Current+1]<Position){ Current++; }
            float Offset=Position-Parabolas[Current];
            Output[Position]=(Offset*Offset)+Samples[Parabolas[Current]];
        }
    }

    /// <summary>Where the parabola rooted at <paramref name="Position"/> rises above the one rooted at <paramref name="Previous"/> (Previous &lt; Position).</summary>
    private static float Intersection(float[]Samples,int Position,int Previous)=>
        ((Samples[Position]+((float)Position*Position))-(Samples[Previous]+((float)Previous*Previous)))/(2f*(Position-Previous));
}