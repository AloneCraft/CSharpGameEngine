namespace CSharpGameEngine.Graphics.D3D12;

public readonly record struct PixelColor(byte R,byte G,byte B,byte A){
    public override string ToString()=> $"RGBA({R},{G},{B},{A})";
}
