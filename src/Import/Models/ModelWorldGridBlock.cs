using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
using CSharpGameEngine.Rendering;
using SharpGLTF.Schema2;
namespace CSharpGameEngine.Import.Models;
internal static class ModelWorldGridBlock {
    private const string Schema="cge.world-grid/1";
    internal static InterchangeBlock?FromGltf(Material Material,IImportContext Context,string Source){
        if(Material.Extras is not JsonObject Extras||!Extras.TryGetPropertyValue("CGE_world_grid",out var Node)){ return null; }
        try {
            if(Node is not JsonObject Grid){ throw new FormatException("grid must be an object"); }
            var Size=Grid["gridSizeMeters"]?.GetValue<float>()??throw new FormatException("gridSizeMeters is missing");
            var Divisions=Grid["subGridDivisions"]?.GetValue<int>()??throw new FormatException("subGridDivisions is missing");
            var Width=Grid["wireHalfWidth"]?.GetValue<float>()??throw new FormatException("wireHalfWidth is missing");
            Vector3 Color(string Key){
                if(Grid[Key] is not JsonArray Values||Values.Count!=3){ throw new FormatException(Key+" must contain three numbers"); }
                var Value=new Vector3(Values[0]!.GetValue<float>(),Values[1]!.GetValue<float>(),Values[2]!.GetValue<float>());
                if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)||Value.X<0f||Value.Y<0f||Value.Z<0f){ throw new FormatException(Key+" must be finite and nonnegative"); }
                return Value;
            }
            var Major=Color("gridColor");var Minor=Color("subGridColor");
            if(!float.IsFinite(Size)||Size<=0f||Divisions<1||!float.IsFinite(Width)||Width<=0f||Width>=0.5f){ throw new FormatException("grid size, divisions or width is invalid"); }
            var Values=ImmutableSortedDictionary.CreateBuilder<string,InterchangeValue>(StringComparer.Ordinal);
            Values.Add("gridSizeMeters",new(InterchangeValueKind.Float,new(Size,0f,0f,0f),null,null));
            Values.Add("subGridDivisions",new(InterchangeValueKind.Text,default,Divisions.ToString(CultureInfo.InvariantCulture),null));
            Values.Add("wireHalfWidth",new(InterchangeValueKind.Float,new(Width,0f,0f,0f),null,null));
            Values.Add("gridColor",new(InterchangeValueKind.Float3,new(Major,0f),null,null));
            Values.Add("subGridColor",new(InterchangeValueKind.Float3,new(Minor,0f),null,null));
            if(Grid["anchorToObject"]?.GetValue<bool>()==true){ Values.Add("anchorToObject",new(InterchangeValueKind.Text,default,"true",null)); }
            return new(Schema,Values.ToImmutable());
        }catch(Exception Error)when(Error is FormatException or InvalidOperationException or JsonException){
            Context.Report(new(ImportSeverity.Warning,"IMP144",Gltf.BuiltInGltfImporter.Identity,ImportPhase.Read,$"Material '{Material.Name}' has an invalid world grid ({Error.Message}); it retains its base surface without the grid.",new(Source)));
            return null;
        }
    }
    internal static StageWorldGrid?Read(ImmutableArray<InterchangeBlock>Blocks){
        var Matches=Blocks.Where(static Block=>Block.Schema==Schema).ToArray();
        if(Matches.Length==0){ return null; }
        if(Matches.Length!=1||Matches[0].Values.Count is not(5 or 6)){ throw new InvalidDataException("IMP144: duplicate or malformed world grid block."); }
        var Values=Matches[0].Values;
        InterchangeValue Value(string Key,InterchangeValueKind Kind){
            if(!Values.TryGetValue(Key,out var Result)||Result.Kind!=Kind){ throw new InvalidDataException("IMP144: invalid world grid field "+Key); }
            return Result;
        }
        if(!int.TryParse(Value("subGridDivisions",InterchangeValueKind.Text).Text,NumberStyles.None,CultureInfo.InvariantCulture,out var Divisions)){ throw new InvalidDataException("IMP144: invalid world grid divisions."); }
        var Major=Value("gridColor",InterchangeValueKind.Float3).Number;var Minor=Value("subGridColor",InterchangeValueKind.Float3).Number;
        var AnchorToObject=Values.Count==6;
        if(AnchorToObject&&Value("anchorToObject",InterchangeValueKind.Text).Text!="true"){ throw new InvalidDataException("IMP144: invalid grid anchor."); }
        return new(Value("gridSizeMeters",InterchangeValueKind.Float).Number.X,Divisions,Value("wireHalfWidth",InterchangeValueKind.Float).Number.X,new(Major.X,Major.Y,Major.Z),new(Minor.X,Minor.Y,Minor.Z),AnchorToObject);
    }
}
