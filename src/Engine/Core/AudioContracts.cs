namespace CSharpGameEngine.Core;

public readonly record struct AudioBusId(StableId Value) {
    public static AudioBusId Master { get; }=new(
        StableId.Parse("fe6db9ef-a5a5-5ad5-9b30-98683d6c44ef"));
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}

public readonly record struct AudioVoiceId(StableId Value) {
    public bool IsValid=>this.Value.IsValid;
    public override string ToString()=>this.Value.ToString();
}
