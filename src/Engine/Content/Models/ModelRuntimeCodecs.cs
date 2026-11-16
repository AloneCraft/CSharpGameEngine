namespace CSharpGameEngine.Content.Models;
public static class ModelRuntimeCodecs {
    public static IEnumerable<IRuntimeAssetCodec>Create()=>[
        Codec(ContentArtifactKind.Model,ModelArtifactCodec.ReadModel),
        Codec(ContentArtifactKind.Mesh,ModelArtifactCodec.ReadMesh),
        Codec(ContentArtifactKind.Skeleton,ModelArtifactCodec.ReadSkeleton),
        Codec(ContentArtifactKind.AnimationClip,ModelArtifactCodec.ReadClip),
        Codec(ContentArtifactKind.License,ModelArtifactCodec.ReadLicense)
    ];
    private delegate T PayloadReader<T>(ReadOnlySpan<byte>Payload);
    private static RuntimeAssetCodec<T>Codec<T>(ContentArtifactKind Kind,PayloadReader<T>Read) where T:notnull=>new(K=>K==Kind,(Artifact,Stop)=>{
        Stop.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(Artifact.Payload.AsSpan()));
    });
}
