namespace CSharpGameEngine.Rendering;
public readonly record struct PixelColor(byte R,byte G,byte B,byte A){
    public override string ToString()=> $"RGBA({this.R},{this.G},{this.B},{this.A})";
}
