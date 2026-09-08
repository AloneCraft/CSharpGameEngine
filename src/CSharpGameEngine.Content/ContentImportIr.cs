using System.Collections.Immutable;
using System.Numerics;

namespace CSharpGameEngine.Content;

public readonly record struct ContentBounds(Vector3 Minimum,Vector3 Maximum) {
    public Vector3 Center=>(this.Minimum+this.Maximum)*0.5f;
    public Vector3 Extents=>(this.Maximum-this.Minimum)*0.5f;
}

/// <summary>Provider-neutral, renderer-ready geometry emitted by every L7 importer adapter.</summary>
public sealed class ContentMeshIr {
    private ContentMeshIr(
        ImmutableArray<Vector3>Positions,
        ImmutableArray<Vector3>Normals,
        ImmutableArray<Vector2>TextureCoordinates,
        ImmutableArray<uint>Indices,
        ContentBounds Bounds){
        this.Positions=Positions;
        this.Normals=Normals;
        this.TextureCoordinates=TextureCoordinates;
        this.Indices=Indices;
        this.Bounds=Bounds;
    }

    public ImmutableArray<Vector3>Positions{get;}
    public ImmutableArray<Vector3>Normals{get;}
    public ImmutableArray<Vector2>TextureCoordinates{get;}
    public ImmutableArray<uint>Indices{get;}
    public ContentBounds Bounds{get;}

    public static ContentMeshIr Create(
        IEnumerable<Vector3>Positions,
        IEnumerable<Vector3>?Normals,
        IEnumerable<Vector2>?TextureCoordinates,
        IEnumerable<uint>Indices){
        if(Positions is null){ throw new ArgumentNullException(nameof(Positions)); }
        if(Indices is null){ throw new ArgumentNullException(nameof(Indices)); }
        ImmutableArray<Vector3>PositionArray=Positions.ToImmutableArray();
        ImmutableArray<Vector3>NormalArray=Normals?.ToImmutableArray()??[];
        ImmutableArray<Vector2>TextureArray=TextureCoordinates?.ToImmutableArray()??[];
        ImmutableArray<uint>IndexArray=Indices.ToImmutableArray();
        if(PositionArray.IsEmpty){ throw new ArgumentException("Imported geometry must contain at least one position.",nameof(Positions)); }
        if(!NormalArray.IsEmpty&&NormalArray.Length!=PositionArray.Length){ throw new ArgumentException("Imported normals must be empty or match the position count.",nameof(Normals)); }
        if(!TextureArray.IsEmpty&&TextureArray.Length!=PositionArray.Length){ throw new ArgumentException("Imported texture coordinates must be empty or match the position count.",nameof(TextureCoordinates)); }
        if(IndexArray.IsEmpty||IndexArray.Length%3!=0){ throw new ArgumentException("Imported indices must contain a non-empty triangle list.",nameof(Indices)); }
        Vector3 Minimum=PositionArray[0];
        Vector3 Maximum=PositionArray[0];
        for(var Index=0;Index<PositionArray.Length;Index++){
            Vector3 Position=PositionArray[Index];
            if(!IsFinite(Position)){ throw new ArgumentException("Imported positions must be finite.",nameof(Positions)); }
            Minimum=Vector3.Min(Minimum,Position);
            Maximum=Vector3.Max(Maximum,Position);
            if(!NormalArray.IsEmpty&&!IsFinite(NormalArray[Index])){ throw new ArgumentException("Imported normals must be finite.",nameof(Normals)); }
            if(!TextureArray.IsEmpty&&!IsFinite(TextureArray[Index])){ throw new ArgumentException("Imported texture coordinates must be finite.",nameof(TextureCoordinates)); }
        }
        foreach(uint Index in IndexArray){
            if(Index>=(uint)PositionArray.Length){ throw new ArgumentException("Imported index refers beyond the position array.",nameof(Indices)); }
        }
        return new ContentMeshIr(
            PositionArray,NormalArray,TextureArray,IndexArray,new ContentBounds(Minimum,Maximum));
    }

    private static bool IsFinite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    private static bool IsFinite(Vector2 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y);
}

public sealed class ContentPbrMaterialIr {
    public ContentPbrMaterialIr(Vector4 BaseColorFactor,byte[]?BaseColorTexture){
        if(!float.IsFinite(BaseColorFactor.X)||!float.IsFinite(BaseColorFactor.Y)
            ||!float.IsFinite(BaseColorFactor.Z)||!float.IsFinite(BaseColorFactor.W)){
            throw new ArgumentException("Base color factor must be finite.",nameof(BaseColorFactor));
        }
        this.BaseColorFactor=BaseColorFactor;
        this.BaseColorTexture=BaseColorTexture is null?null:ImmutableArray.Create(BaseColorTexture);
    }
    public Vector4 BaseColorFactor{get;}
    public ImmutableArray<byte>?BaseColorTexture{get;}
}

public enum ContentImportDiagnosticSeverity { Info,Warning,Error }
public sealed record ContentImportDiagnostic(
    ContentImportDiagnosticSeverity Severity,
    string Provider,
    string Phase,
    string Message);

public sealed class ContentMeshImportResult {
    private ContentMeshImportResult(
        ContentMeshIr?Mesh,
        ContentPbrMaterialIr?Material,
        ImmutableArray<ContentImportDiagnostic>Diagnostics){
        this.Mesh=Mesh;
        this.Material=Material;
        this.Diagnostics=Diagnostics;
    }
    public bool Success=>this.Mesh is not null&&this.Material is not null
        &&!this.Diagnostics.Any(static Diagnostic=>Diagnostic.Severity==ContentImportDiagnosticSeverity.Error);
    public ContentMeshIr?Mesh{get;}
    public ContentPbrMaterialIr?Material{get;}
    public ImmutableArray<ContentImportDiagnostic>Diagnostics{get;}
    public static ContentMeshImportResult Succeeded(
        ContentMeshIr Mesh,ContentPbrMaterialIr Material,
        IEnumerable<ContentImportDiagnostic>?Diagnostics=null)=>new(
            Mesh??throw new ArgumentNullException(nameof(Mesh)),
            Material??throw new ArgumentNullException(nameof(Material)),
            Diagnostics?.ToImmutableArray()??[]);
    public static ContentMeshImportResult Failed(IEnumerable<ContentImportDiagnostic>Diagnostics){
        if(Diagnostics is null){ throw new ArgumentNullException(nameof(Diagnostics)); }
        ImmutableArray<ContentImportDiagnostic>Values=Diagnostics.ToImmutableArray();
        if(!Values.Any(static Diagnostic=>Diagnostic.Severity==ContentImportDiagnosticSeverity.Error)){
            throw new ArgumentException("A failed import requires at least one error diagnostic.",nameof(Diagnostics));
        }
        return new ContentMeshImportResult(null,null,Values);
    }
}
