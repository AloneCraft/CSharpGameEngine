namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureStageCatalog {
    public const string CatalogSchemaVersion="csharp-game-engine-feature-catalog-v2";
    public const string ExtendedEcsMaskFeatureId="F-L3-020";
    public static IReadOnlyList<string>NonPhysicsRoadmapFeatureIds{get;}=[
        "F-L7-022","F-L2-005","F-L7-049","F-L6-004","F-L4-004","F-L0-008","F-L4-005","F-L4-006","F-L4-007","F-L0-009","F-L8-018","F-L7-023","F-L7-024",
        "F-L2-004","F-L7-025","F-L6-005","F-L7-026","F-L7-027","F-L4-008","F-L6-006","F-L8-019","F-L7-028","F-L7-029",
        "F-L8-020","F-L8-021","F-L8-022","F-L8-031","F-L8-023","F-L8-024","F-L8-025","F-L8-026","F-L8-027","F-L8-028","F-L8-029",
        "F-L7-030","F-L3-018","F-L5-035","F-L3-021","F-L3-022","F-L3-019","F-L7-031","F-L6-007","F-L7-032","F-L7-033",
        "F-L1-008","F-L0-010","F-L4-009","F-L4-010","F-L4-011","F-L4-012","F-L8-030","F-L7-034","F-L7-035",
        "F-L9-009","F-L7-036","F-L7-037","F-L7-038","F-L7-039","F-L7-040","F-L7-041","F-L7-042","F-L7-043","F-L7-044","F-L7-045","F-L7-046","F-L7-047","F-L7-048",
    ];
    public static IReadOnlyList<string>ExistingFeatureIds{get;}=[
        "F-L7-001","F-L7-002","F-L7-012","F-L7-013","F-L7-015","F-L7-016","F-L7-017",
        "F-L1-001","F-L1-002","F-L1-003","F-L1-005","F-L1-006","F-L2-003","F-L0-006",
        "F-L0-003","F-L0-005","F-L2-002","F-L7-005",
        "F-L0-001","F-L0-002","F-L0-004","F-L0-007","F-L4-002",
        "F-L3-011","F-L3-012","F-L3-013",
        "F-L1-007","F-L4-001",
        "F-L8-001","F-L8-002","F-L8-005","F-L8-008",
        "F-L8-010","F-L8-011","F-L8-012","F-L8-015","F-L8-016","F-L8-017",
        "F-L3-015","F-L3-016","F-L3-017",
        "F-L5-001","F-L5-002","F-L5-003","F-L3-018","F-L6-002","F-L7-020","F-L8-014",
    ];
    public static IReadOnlyList<string>DeferredSimulationFeatureIds{get;}=[
        "F-L5-004","F-L5-005","F-L5-006","F-L5-007","F-L5-008","F-L5-009","F-L5-010","F-L5-011",
        "F-L5-012","F-L5-013","F-L5-014","F-L5-015","F-L5-016","F-L5-017","F-L5-018","F-L5-019",
        "F-L5-020","F-L5-021","F-L5-022","F-L5-023","F-L5-024","F-L5-025","F-L5-026","F-L5-027",
        "F-L5-028","F-L5-029","F-L5-030","F-L5-031",
    ];
    public IReadOnlyList<FeatureDefinition>Features{get;}
    public IReadOnlyList<FeatureStageDefinition>Stages{get;}
    private FeatureStageCatalog(IReadOnlyList<FeatureDefinition>Features,IReadOnlyList<FeatureStageDefinition>Stages){
        this.Features=Features;
        this.Stages=Stages;
    }
    public static FeatureStageCatalog CreateDefault(){
        var Features=new List<FeatureDefinition>();
        AddExistingFeatures(Features);
        AddInfrastructureFeatures(Features);
        AddNonPhysicsRoadmapFeatures(Features);
        AddDeferredSimulationFeatures(Features);
        var Stages=new List<FeatureStageDefinition>();
        AddExistingStages(Stages);
        AddInfrastructureStages(Stages);
        AddNonPhysicsRoadmapStages(Stages);
        AddDeferredSimulationStages(Stages);
        return new FeatureStageCatalog(Features,Stages);
    }
    public FeatureDefinition GetFeature(string FeatureId)=>this.Features.FirstOrDefault(Feature=>Feature.Id==FeatureId)??throw new KeyNotFoundException($"Unknown feature ID: {FeatureId}");
    public FeatureStageDefinition GetStage(string StageId)=>this.Stages.FirstOrDefault(Stage=>Stage.Id==StageId)??throw new KeyNotFoundException($"Unknown stage ID: {StageId}");
    public IReadOnlyList<string>Validate(){
        var Errors=new List<string>();
        foreach(var Group in this.Features.GroupBy(static Feature=>Feature.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){
            Errors.Add($"Duplicate feature ID: {Group.Key}");
        }
        foreach(var Group in this.Stages.GroupBy(static Stage=>Stage.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){
            Errors.Add($"Duplicate stage ID: {Group.Key}");
        }
        var FeatureIds=new HashSet<string>(this.Features.Select(static Feature=>Feature.Id));
        var FeaturesById=this.Features.GroupBy(static Feature=>Feature.Id,StringComparer.Ordinal).ToDictionary(static Group=>Group.Key,static Group=>Group.First(),StringComparer.Ordinal);
        foreach(var Feature in this.Features){
            if(Feature.DependencyLevel<0||Feature.DependencyLevel>7){ Errors.Add($"Feature dependency level must be between zero and seven: {Feature.Id}"); }
            foreach(var DependencyId in Feature.DependsOnFeatureIds){
                if(DependencyId==Feature.Id){ Errors.Add($"Feature has a self dependency: {Feature.Id}"); }
                if(!FeaturesById.TryGetValue(DependencyId,out var Dependency)){ Errors.Add($"Feature {Feature.Id} depends on unknown feature {DependencyId}"); }
                else if(Dependency.DependencyLevel>Feature.DependencyLevel){ Errors.Add($"Feature {Feature.Id} depends on higher-level feature {DependencyId}"); }
            }
        }
        ValidateDependencyCycles(this.Features,FeaturesById,Errors);
        foreach(var Stage in this.Stages){
            if(Stage.FeatureIds.Count==0){ Errors.Add($"Stage has no feature: {Stage.Id}"); }
            foreach(var FeatureId in Stage.FeatureIds.Where(FeatureId=>!FeatureIds.Contains(FeatureId))){ Errors.Add($"Stage {Stage.Id} references unknown feature {FeatureId}"); }
            if(Stage.Availability==FeatureStageAvailability.Executable&&Stage.Commands.Count==0){ Errors.Add($"Executable stage has no command: {Stage.Id}"); }
            if(Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.Commands.Count!=0){ Errors.Add($"NotImplemented stage has commands: {Stage.Id}"); }
            if(Stage.Mode==FeatureStageMode.Visual&&Stage.AdapterRequirement!=FeatureAdapterRequirement.WarpRequiredHardwareOptional){ Errors.Add($"Visual stage must require WARP: {Stage.Id}"); }
            if(Stage.Mode==FeatureStageMode.Visual&&(string.IsNullOrWhiteSpace(Stage.SceneProject)||string.IsNullOrWhiteSpace(Stage.SceneId))){ Errors.Add($"Visual stage must bind a C# scene: {Stage.Id}"); }
            if(Stage.Seed==0){ Errors.Add($"Stage seed must be nonzero: {Stage.Id}"); }
            if(Stage.Ticks<=0){ Errors.Add($"Stage ticks must be positive: {Stage.Id}"); }
            if(Stage.TimeoutSeconds<=0){ Errors.Add($"Stage timeout must be positive: {Stage.Id}"); }
            if(string.IsNullOrWhiteSpace(Stage.Oracle)){ Errors.Add($"Stage oracle is required: {Stage.Id}"); }
        }
        foreach(var Feature in this.Features.Where(Feature=>!this.Stages.Any(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)))){
            Errors.Add($"Feature has no stage: {Feature.Id}");
        }
        foreach(var Feature in this.Features){
            var FeatureStages=this.Stages.Where(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)).ToArray();
            if((Feature.Status is FeatureLifecycleStatus.Implemented or FeatureLifecycleStatus.PartiallyImplemented or FeatureLifecycleStatus.Implementing)&&!FeatureStages.Any(static Stage=>Stage.Availability==FeatureStageAvailability.Executable)){
                Errors.Add($"Implemented feature has no executable stage: {Feature.Id}");
            }
            if((Feature.Status is FeatureLifecycleStatus.Specified or FeatureLifecycleStatus.Deferred)&&!FeatureStages.Any(static Stage=>Stage.Availability==FeatureStageAvailability.NotImplemented)){
                Errors.Add($"Specified or deferred feature has no NotImplemented stage: {Feature.Id}");
            }
        }
        return Errors;
    }
    private static void ValidateDependencyCycles(IReadOnlyList<FeatureDefinition>Features,IReadOnlyDictionary<string,FeatureDefinition>FeaturesById,List<string>Errors){
        var States=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var Feature in Features){ Visit(Feature); }
        void Visit(FeatureDefinition Feature){
            if(States.TryGetValue(Feature.Id,out var State)){
                if(State==1){ Errors.Add($"Feature dependency cycle includes {Feature.Id}"); }
                return;
            }
            States.Add(Feature.Id,1);
            foreach(var DependencyId in Feature.DependsOnFeatureIds){
                if(FeaturesById.TryGetValue(DependencyId,out var Dependency)){ Visit(Dependency); }
            }
            States[Feature.Id]=2;
        }
    }
    private static void AddExistingFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-001","CodeAnalysis project foundation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-002","Bootstrap source generator","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-012","Scene DSL analysis and runtime generation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-013","Scene DSL diagnostics","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-015","Game system source generator","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-016","Game system diagnostics","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-017","Startup scene system generation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-001","RUF semantic coordinates","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-002","RUF quaternion and transform conventions","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-003","Engine coordinate convention presets","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-005","glTF coordinate convention","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-006","FBX coordinate convention","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L2-003","Camera and light basis schema","L2",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-006","Shared camera view matrix","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-003","Procedural mesh factory","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-005","glTF scene importer","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L2-002","Geometry primitives","L2",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-005","Unreal level import pipeline","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-001","Windows camera runtime","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-002","D3D12 stage renderer","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-004","Win32 input backend","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-007","Directional shadows HDR ACES and bloom","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L4-002","Windows render server","L4",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-011","Scene DSL runtime contracts","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-012","Closed-set archetype runtime","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-013","Startup scene contract","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-007","Audio backend abstraction","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L4-001","XAudio2 backend","L4",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-001","Open game command","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-002","VSIX glTF bridge","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-005","Game Engine layout","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-008","Resident bridge process","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-010","Scene Designer model rendering","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-011","Scene editor interaction","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-012","Scene inspector editing","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-015","VSIX localization","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-016","Scene View physics sidebar","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-017","Scene Designer Escape routing","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-015","Entity loose octree","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-016","World spatial snapshot","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-017","Spatial debug projection","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-001","Shared Windows ECS runtime","L5",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-002","Deterministic simulation runtime","L5",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-003","Deterministic rigid-body runtime","L5",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L3-018","System-major fixed simulation phases","L3",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.EcsRuntime,2,["F-L7-030"]);
        Add(Features,"F-L6-002","Physics cooking and cache artifacts","L6",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L7-020","Physics CLI and bake host","L7",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L8-014","Scene View physics controls and overlays","L8",FeatureLifecycleStatus.PartiallyImplemented);
    }
    private static void AddInfrastructureFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-021","Feature Runner","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L9-008","Feature stage packs","L9",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L9-009","Engine Showcase and live feature progress","L9",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.TestingCiRelease,4,["F-L7-030","F-L3-018","F-L7-022","F-L6-004","F-L4-010","F-L5-003"]);
        Add(Features,"F-L0-008","D3D12 compute and readback","L0",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,2,["F-L7-022"]);
        Add(Features,"F-L3-019","Deterministic job runtime","L3",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,4,["F-L3-022"]);
        Add(Features,ExtendedEcsMaskFeatureId,"Extended static ECS component mask","L3",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L4-003","Simulation backend contracts and adapters","L4",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L6-003","Field and graph artifacts","L6",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-032","Animation physics pose bridge","L5",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-033","Volume and field renderer","L5",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-034","Thermal field domain","L5",FeatureLifecycleStatus.Deferred);
    }
    private static void AddRenderingRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-022","Rendering baseline conformance","L7",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.Rendering,1,[]);
        Add(Features,"F-L2-005","Shader Graph canonical schema","L2",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.Rendering,1,["F-L7-022"]);
        Add(Features,"F-L7-049","Shader Graph compiler","L7",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.Rendering,2,["F-L2-005"]);
        Add(Features,"F-L4-004","Engine-owned render graph","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,2,["F-L7-022"]);
        Add(Features,"F-L6-004","Shader artifact compiler and PSO provenance","L6",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.Rendering,3,["F-L7-022","F-L7-049"]);
        Add(Features,"F-L4-005","Material and texture runtime","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,3,["F-L6-004"]);
        Add(Features,"F-L4-006","Visibility, LOD, and instancing","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,4,["F-L4-004","F-L4-005"]);
        Add(Features,"F-L4-007","Rendering quality profiles","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,4,["F-L4-006"]);
        Add(Features,"F-L0-009","D3D12 device recovery","L0",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,5,["F-L4-004","F-L4-007"]);
        Add(Features,"F-L8-018","Shared isolated rendering preview","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,5,["F-L0-009","F-L8-022"]);
        Add(Features,"F-L7-023","Rendering performance evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,6,["F-L4-007","F-L0-009"]);
        Add(Features,"F-L7-024","Rendering release evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.Rendering,7,["F-L7-023","F-L8-018"]);
    }
    private static void AddAssetContentRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L2-004","Asset identity and canonical metadata","L2",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,1,[]);
        Add(Features,"F-L7-025","Importer conformance","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,2,["F-L2-004"]);
        Add(Features,"F-L6-005","Content artifact envelope","L6",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,3,["F-L7-025"]);
        Add(Features,"F-L7-026","Incremental content compiler","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,4,["F-L6-005"]);
        Add(Features,"F-L7-027","Content cache recovery and GC","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,4,["F-L7-026"]);
        Add(Features,"F-L4-008","Asynchronous runtime asset handles","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,5,["F-L7-027"]);
        Add(Features,"F-L6-006","Shipping content manifest","L6",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,5,["F-L4-008"]);
        Add(Features,"F-L8-019","VSIX asset authoring","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,6,["F-L6-006","F-L8-022"]);
        Add(Features,"F-L7-028","Content pipeline performance evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,6,["F-L6-006"]);
        Add(Features,"F-L7-029","Content pipeline release evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AssetContent,7,["F-L7-028","F-L8-019"]);
    }
    private static void AddEditorVsixRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L8-031","Visual Shader Graph editor","L8",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.EditorVsix,3,["F-L7-049","F-L8-021"]);
        Add(Features,"F-L8-020","VSIX manual and Experimental Instance baseline","L8",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.EditorVsix,1,[]);
        Add(Features,"F-L8-021","VSIX Experimental Instance automation","L8",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.EditorVsix,2,["F-L8-020"]);
        Add(Features,"F-L8-022","Versioned authoring host lifecycle","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,3,["F-L8-021"]);
        Add(Features,"F-L8-023","Transform tool visual conformance","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,4,["F-L8-022"]);
        Add(Features,"F-L8-024","Typed inspector and component palette","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,4,["F-L8-022"]);
        Add(Features,"F-L8-025","C# source synchronization v2","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,5,["F-L8-023","F-L8-024"]);
        Add(Features,"F-L8-026","Bake, asset, and diagnostic UI","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,5,["F-L8-025"]);
        Add(Features,"F-L8-027","Read-only runtime debug tools","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,6,["F-L8-026"]);
        Add(Features,"F-L8-028","VSIX compatibility matrix","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,6,["F-L8-027"]);
        Add(Features,"F-L8-029","VSIX release evidence","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EditorVsix,7,["F-L8-028"]);
    }
    private static void AddEcsRuntimeRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-030","ECS runtime baseline conformance","L7",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.EcsRuntime,1,[]);
        Add(Features,"F-L5-035","Portable shared Transform module","L5",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,2,["F-L7-030"]);
        Add(Features,"F-L3-021","Unified structural command buffer","L3",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,3,["F-L3-018","F-L5-035"]);
        Add(Features,"F-L3-022","Enable masks and change versions","L3",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,4,["F-L3-021"]);
        Add(Features,"F-L7-031","Generated schedule diagnostics","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,5,["F-L3-019"]);
        Add(Features,"F-L6-007","Schema-fingerprinted ECS snapshots","L6",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,5,["F-L3-022","F-L3-019"]);
        Add(Features,"F-L7-032","ECS runtime performance evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,6,["F-L7-031","F-L6-007"]);
        Add(Features,"F-L7-033","ECS runtime release evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.EcsRuntime,7,["F-L7-032"]);
    }
    private static void AddAudioInputPlatformRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L1-008","Platform event and timestamp contracts","L1",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.AudioInputPlatform,1,[]);
        Add(Features,"F-L0-010","Win32 window and input binding","L0",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.AudioInputPlatform,2,["F-L1-008"]);
        Add(Features,"F-L4-009","Audio device lifecycle server","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,2,["F-L1-008"]);
        Add(Features,"F-L4-010","Deterministic input action server","L4",FeatureLifecycleStatus.Implemented,FeatureRoadmapTrack.AudioInputPlatform,3,["F-L0-010"]);
        Add(Features,"F-L4-011","Audio mixer and offline oracle","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,3,["F-L4-009"]);
        Add(Features,"F-L4-012","Spatial audio and recovery","L4",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,4,["F-L4-010","F-L4-011"]);
        Add(Features,"F-L8-030","Platform diagnostics viewer","L8",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,5,["F-L4-012"]);
        Add(Features,"F-L7-034","Platform compatibility evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,6,["F-L8-030"]);
        Add(Features,"F-L7-035","Platform release evidence","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.AudioInputPlatform,7,["F-L7-034"]);
    }
    private static void AddTestingCiReleaseRoadmapFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-036","Feature coverage and catalog validation","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,1,["F-L7-021"]);
        Add(Features,"F-L7-037","Manual verification ledger","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,1,["F-L7-036"]);
        Add(Features,"F-L7-038","Changed-path stage selection","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,2,["F-L7-036"]);
        Add(Features,"F-L7-039","Hermetic clean-build verification","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,2,["F-L7-038"]);
        Add(Features,"F-L7-040","Golden artifact policy","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,3,["F-L7-039"]);
        Add(Features,"F-L7-041","Performance measurement framework","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,4,["F-L7-040"]);
        Add(Features,"F-L7-042","Explicit CI matrix","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,4,["F-L7-041"]);
        Add(Features,"F-L7-043","Failure, retry, and quarantine policy","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,5,["F-L7-042"]);
        Add(Features,"F-L7-044","Stress contract","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,5,["F-L7-043"]);
        Add(Features,"F-L7-045","Package install and SBOM validation","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,6,["F-L7-044"]);
        Add(Features,"F-L7-046","Release compatibility matrix","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,6,["F-L7-045"]);
        Add(Features,"F-L7-047","Release candidate gate","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,7,["F-L7-046"]);
        Add(Features,"F-L7-048","Immutable release manifest","L7",FeatureLifecycleStatus.Specified,FeatureRoadmapTrack.TestingCiRelease,7,["F-L7-047"]);
    }
    private static void AddNonPhysicsRoadmapFeatures(List<FeatureDefinition>Features){
        AddRenderingRoadmapFeatures(Features);
        AddAssetContentRoadmapFeatures(Features);
        AddEditorVsixRoadmapFeatures(Features);
        AddEcsRuntimeRoadmapFeatures(Features);
        AddAudioInputPlatformRoadmapFeatures(Features);
        AddTestingCiReleaseRoadmapFeatures(Features);
    }
    private static void AddDeferredSimulationFeatures(List<FeatureDefinition>Features){
        var Names=new[]{"Joints","Ragdoll","Character controller","Vehicle","Dynamic-dynamic CCD","Parallel solver","GPU solver","Rollback","PBD and XPBD","Cloth","Rope","Soft body","FEM","MPM","SPH and PBF","FLIP and APIC","Eulerian grid fluid","Runtime Voronoi and Boolean destruction","Voxel and SDF destruction","Kepler orbit","Direct N-body","Barnes-Hut gravity","Fast multipole gravity","Hierarchical celestial coordinates","Gameplay probability domain","Wavefunction grid","Monte Carlo scientific quantum domain","Cross-domain coupling"};
        for(var Index=0;Index<DeferredSimulationFeatureIds.Count;Index++){
            Add(Features,DeferredSimulationFeatureIds[Index],Names[Index],"L5",FeatureLifecycleStatus.Deferred);
        }
    }
    private static void AddExistingStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"foundation.codegen","Source generator contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-001","F-L7-002","F-L7-012","F-L7-013","F-L7-015","F-L7-016","F-L7-017"],[Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj")],["src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"All generator and analyzer tests exit with code zero.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"foundation.core-spatial","Core coordinate contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L1-001","F-L1-002","F-L1-003","F-L1-005","F-L1-006","F-L2-003","F-L0-006"],[Test("tests/CSharpGameEngine.Numerics.Tests/CSharpGameEngine.Numerics.Tests.csproj"),Test("tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj")],["src/CSharpGameEngine.Core/","src/CSharpGameEngine.Numerics/","tests/CSharpGameEngine.Numerics.Tests/","tests/CSharpGameEngine.Core.Tests/"],"Core and numerical contract suites exit with code zero.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"geometry.import","Geometry and import contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L0-003","F-L0-005","F-L2-002","F-L7-005"],[Test("tests/CSharpGameEngine.Import.Tests/CSharpGameEngine.Import.Tests.csproj")],["src/CSharpGameEngine.Import/","tests/CSharpGameEngine.Import.Tests/","tools/CSharpGameEngine.Unreal.SceneImporter/"],"Import fixtures and coordinate conversions match their expected artifacts.",900,["changed","smoke","full"]);
        AddExecutable(Stages,"rendering.warp-triangle","D3D12 WARP visual readback",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L0-001","F-L0-002","F-L0-004","F-L0-007","F-L4-002"],[Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","rendering.warp-triangle"),new FeatureStageCommand("dotnet",["run","--project","tools/CSharpGameEngine.TestHost/CSharpGameEngine.TestHost.csproj","--configuration","{configuration}","--","triangle","--hidden","--frames","3","--debug-layer","--verify"],["--warp"],[])],["src/CSharpGameEngine.Windows/","src/CSharpGameEngine.Runtime.Windows/","tools/CSharpGameEngine.TestHost/","stages/CSharpGameEngine.FeatureStages.Rendering/","assets/Shaders/"],"C# Scene instantiate/unload succeeds, readback contains the rendered triangle, and the D3D12 debug layer reports no errors.",900,["changed","smoke","full","stress"],FeatureAdapterRequirement.WarpRequiredHardwareOptional);
        AddExecutable(Stages,"ecs.scene","Static ECS and scene lifecycle",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-011","F-L3-012","F-L3-013"],[Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj","FullyQualifiedName~Scene|FullyQualifiedName~Archetype|FullyQualifiedName~Startup")],["src/CSharpGameEngine.Entities/","src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.Entities.Tests/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"Generated archetype, scene, and startup lifecycle contracts pass.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"audio.windows","Audio abstraction and XAudio2 contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L1-007","F-L4-001"],[Test("tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj","FullyQualifiedName~Audio"),Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Audio")],["src/CSharpGameEngine.Core/Audio/","src/CSharpGameEngine.Windows/Audio/","tests/CSharpGameEngine.Core.Tests/Audio/"],"Audio abstraction and backend contract tests pass without requiring audible output.",900,["changed","smoke","full"]);
        AddExecutable(Stages,"vsix.toolchain","VSIX command and bridge contracts",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-001","F-L8-002","F-L8-005","F-L8-008"],[Test("tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj")],["tools/CSharpGameEngine.VSExtension/","tests/CSharpGameEngine.VSExtension.Tests/","tools/CSharpGameEngine.Bridge.Hosting/"],"VSIX model, package, and bridge tests pass.",1800,["changed","smoke","full","vsix"]);
        AddExecutable(Stages,"vsix.scene-editor","Scene editor contracts",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-010","F-L8-011","F-L8-012","F-L8-015","F-L8-016","F-L8-017"],[Test("tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj")],["tools/CSharpGameEngine.VSExtension/","tests/CSharpGameEngine.VSExtension.Tests/","Docs/Specifications/Scene-Editor-Design.md"],"Scene editor state, editing, localization, layout, and Escape routing tests pass.",1800,["changed","smoke","full","vsix"]);
        AddExecutable(Stages,"spatial.runtime","Spatial runtime and debug contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-015","F-L3-016","F-L3-017"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Spatial"),Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj","FullyQualifiedName~Spatial")],["src/CSharpGameEngine.Entities/Spatial","src/CSharpGameEngine.CodeAnalysis/Spatial","tests/CSharpGameEngine.Windows.Tests/Runtime/Spatial"],"Octree, world snapshot, and debug projection tests pass.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"rigid.physics","Deterministic physics foundation",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-001","F-L5-002","F-L5-003","F-L3-018","F-L6-002","F-L7-020","F-L8-014"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Physics|FullyQualifiedName~Simulation")],["src/CSharpGameEngine.Runtime.Simulation/","src/CSharpGameEngine.Runtime.Physics/","tools/CSharpGameEngine.Physics.Cli/","tests/CSharpGameEngine.Windows.Tests/Physics/"],"Deterministic state, message, rigid-body, cooking, cache, and debug contracts pass.",1800,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"runner.contract","Feature Runner catalog and result contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-021"],[Test("tests/CSharpGameEngine.FeatureRunner.Tests/CSharpGameEngine.FeatureRunner.Tests.csproj")],["tools/CSharpGameEngine.FeatureRunner/","tests/CSharpGameEngine.FeatureRunner.Tests/","Docs/Feature-Stages.md"],"Catalog coverage, CLI selection, and versioned result serialization tests pass.",600,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"stage-packs.contract","C# IScene Stage Pack contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L9-008"],[Scene("stages/CSharpGameEngine.FeatureStages.Foundation/CSharpGameEngine.FeatureStages.Foundation.csproj","foundation.reference"),Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","rendering.warp-triangle"),Scene("stages/CSharpGameEngine.FeatureStages.Rigid/CSharpGameEngine.FeatureStages.Rigid.csproj","rigid.joints"),Scene("stages/CSharpGameEngine.FeatureStages.Deformable/CSharpGameEngine.FeatureStages.Deformable.csproj","deformable.xpbd"),Scene("stages/CSharpGameEngine.FeatureStages.Fluid/CSharpGameEngine.FeatureStages.Fluid.csproj","fluid.sph-pbf"),Scene("stages/CSharpGameEngine.FeatureStages.Destruction/CSharpGameEngine.FeatureStages.Destruction.csproj","destruction.runtime-topology"),Scene("stages/CSharpGameEngine.FeatureStages.Celestial/CSharpGameEngine.FeatureStages.Celestial.csproj","celestial.kepler"),Scene("stages/CSharpGameEngine.FeatureStages.ProbabilityQuantum/CSharpGameEngine.FeatureStages.ProbabilityQuantum.csproj","probability.gameplay"),Scene("stages/CSharpGameEngine.FeatureStages.Coupling/CSharpGameEngine.FeatureStages.Coupling.csproj","coupling.multidomain"),Scene("stages/CSharpGameEngine.FeatureStages.Vsix/CSharpGameEngine.FeatureStages.Vsix.csproj","vsix.scene-editor-visual")],["stages/"],"Every isolated Pack compiles its C# IScene, instantiates generated ECS entities, verifies ownership, unloads them, and emits a stable state hash.",1200,["changed","smoke","full","stress","vsix"]);
    }
    private static void AddInfrastructureStages(List<FeatureStageDefinition>Stages){
        AddSpecified(Stages,"foundation.d3d12-compute","D3D12 compute and readback",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L0-008"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Windows/"],"Known compute kernel output must match the CPU vector and debug validation must remain clean.",1800,["full","stress"]);
        AddSpecified(Stages,"foundation.deterministic-jobs","Deterministic job runtime",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-019"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.CodeAnalysis/"],"Single-thread and scheduled execution must produce identical state and event hashes.",900,["full","stress"]);
        AddDeferred(Stages,"foundation.extended-ecs-mask","Extended static ECS mask",FeatureStagePack.Foundation,FeatureStageMode.Headless,[ExtendedEcsMaskFeatureId],"1-64 components retain the single-mask generated path; 65-128 use two generator-time masks; 129 emits an explicit diagnostic.");
        AddDeferred(Stages,"foundation.simulation-adapters","Simulation adapter boundaries",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-003"],"Provider conformance vectors pass without exposing provider types above L4.");
        AddDeferred(Stages,"foundation.field-artifacts","Field and graph artifacts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-003"],"Version, content hash, dependency hash, units, and provenance round-trip exactly.");
        AddDeferred(Stages,"deformable.pose-bridge","Animation physics pose bridge",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-032"],"Animation-to-physics and physics-to-animation handoff occurs only at the declared tick boundary.");
        AddDeferred(Stages,"fluid.volume-renderer","Volume and field renderer",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-033"],"WARP readback matches the reference density and transfer-function image tolerance.");
        AddDeferred(Stages,"coupling.thermal-field","Thermal field domain",FeatureStagePack.Coupling,FeatureStageMode.Visual,["F-L5-034"],"Diffusion reference case remains within the declared energy and L2 error budgets.");
    }
    private static void AddRenderingRoadmapStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"rendering.baseline","Rendering baseline conformance",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L7-022"],[Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","rendering.baseline"),Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~GpuIntegrationTests")],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Windows/","src/CSharpGameEngine.Runtime.Windows/","tests/CSharpGameEngine.Windows.Tests/Graphics/"],"Triangle, Model, Stage, light, shadow, HDR, bloom, and ACES WARP readbacks match the accepted baseline with no D3D12 debug-layer errors.",1800,["changed","full","stress"],FeatureAdapterRequirement.WarpRequiredHardwareOptional);
        AddExecutable(Stages,"showcase.visible-progress","Visible engine showcase and live feature progress",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L9-009"],[Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","showcase.visible-progress"),new FeatureStageCommand("dotnet",["run","--project","tools/CSharpGameEngine.Showcase.Automation/CSharpGameEngine.Showcase.Automation.csproj","--configuration","{configuration}","--","run","--configuration","{configuration}","--adapter","warp","--locale","auto","--timeout-seconds","180","--evidence-dir",".artifacts/Showcase"],[],[],[2])],["UserEcsProject/","src/CSharpGameEngine.Core/InputContracts.cs","src/CSharpGameEngine.Windows/Input/","src/CSharpGameEngine.Windows/Platform/Win32DashboardPanel.cs","tools/CSharpGameEngine.Showcase.Automation/"],"The WARP application exposes all four dashboard pages, deterministic Pause/Step behavior, catalog-derived progress, shader provenance, and owned-process normal shutdown with screenshot evidence.",900,["changed","smoke","full","stress"],FeatureAdapterRequirement.WarpRequiredHardwareOptional);
        AddSpecified(Stages,"rendering.render-graph","Engine-owned render graph",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-004"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Runtime.Windows/"],"Pass dependencies topologically sort, resource read/write hazards produce barriers, and transient aliases never overlap live lifetimes.",900,["changed","full","stress"]);
        AddExecutable(Stages,"shadergraph.schema","Shader Graph canonical schema",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L2-005"],[Test("tests/CSharpGameEngine.ShaderGraph.Tests/CSharpGameEngine.ShaderGraph.Tests.csproj")],["Docs/Specifications/Shader-Graph.md","src/CSharpGameEngine.ShaderGraph/","tests/CSharpGameEngine.ShaderGraph.Tests/"],"Canonical UTF-8 JSON, stable IDs, layout-independent semantic hashes, typed ports, structured If/ForRange/Return regions, branch isolation, cycles, and subgraph recursion obey schema v1.",900,["changed","full","stress"]);
        AddExecutable(Stages,"shadergraph.compiler","Shader Graph compiler",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-049"],[Test("tests/CSharpGameEngine.ShaderGraph.Cli.Tests/CSharpGameEngine.ShaderGraph.Cli.Tests.csproj")],["Docs/Specifications/Shader-Graph.md","tools/CSharpGameEngine.ShaderGraph.Cli/","assets/Shaders/"],"The Stage, Model, and Triangle graphs reproduce deterministic HLSL and compile all nine fixed-ABI DXIL entries with source-map diagnostics.",1800,["changed","full","stress"]);
        AddExecutable(Stages,"rendering.shader-artifacts","Shader artifact compiler and provenance",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-004"],[Test("tests/CSharpGameEngine.ShaderArtifacts.Tests/CSharpGameEngine.ShaderArtifacts.Tests.csproj"),Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~ShaderArtifact")],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.ShaderArtifacts/","src/CSharpGameEngine.Windows/","assets/Shaders/"],"DXIL, fixed ABI reflection, checksums, dependency hash, PSO provenance, artifact-only runtime loading, and last-known-good reload are verified without runtime DXC.",1800,["changed","full","stress"]);
        AddSpecified(Stages,"rendering.materials","Material and texture runtime",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L4-005"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Runtime.Windows/","assets/"],"Material bindings, texture residency, and CPU reference shading match the accepted WARP image tolerance.",1800,["full","stress"]);
        AddSpecified(Stages,"rendering.visibility","Visibility, LOD, and instancing",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L4-006"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Runtime.Windows/","src/CSharpGameEngine.Entities/"],"Stable-ID snapshot extraction, frustum results, LOD hysteresis, and instance batches match the CPU reference.",1800,["full","stress"]);
        AddSpecified(Stages,"rendering.quality-profiles","Rendering quality profiles",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L4-007"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Runtime.Windows/"],"Cascade, IBL, exposure, anti-aliasing, and quality-profile selection meet their declared WARP and CPU reference tolerances.",1800,["full","stress"]);
        AddSpecified(Stages,"rendering.device-recovery","D3D12 device recovery",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L0-009"],["Docs/Specifications/Rendering.md","src/CSharpGameEngine.Windows/","src/CSharpGameEngine.Runtime.Windows/"],"Ready, Suspended, Recovering, Faulted, and Disposed transitions are legal and GPU resources rebuild only from artifacts.",1800,["full","stress"]);
        AddSpecified(Stages,"vsix.rendering-preview","Shared isolated rendering preview",FeatureStagePack.Vsix,FeatureStageMode.Visual,["F-L8-018"],["Docs/Specifications/Rendering.md","Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.EditorPreview.Host/","tools/CSharpGameEngine.VSExtension/"],"The Preview Host uses the Render Server protocol, survives cancel and crash, and never creates a second VSIX renderer.",1800,["full","vsix"]);
        AddSpecified(Stages,"rendering.performance","Rendering performance evidence",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L7-023"],["Docs/Specifications/Rendering.md","tools/CSharpGameEngine.FeatureRunner/"],"Thirty post-warm-up samples record median, p95, MAD, and machine fingerprint within the declared rendering budget.",7200,["full","stress"]);
        AddSpecified(Stages,"rendering.release","Rendering release evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-024"],["Docs/Specifications/Rendering.md","Docs/Specifications/Testing-CI-Release.md"],"The scoped rendering release manifest contains the dependency closure, WARP evidence, provenance, and no unresolved required stage.",7200,["full","stress"]);
    }
    private static void AddAssetContentRoadmapStages(List<FeatureStageDefinition>Stages){
        AddSpecified(Stages,"content.identity","Asset identity and canonical metadata",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L2-004"],["Docs/Specifications/Asset-Content-Pipeline.md","src/CSharpGameEngine.Core/","assets/"],"AssetId and SHA-256-derived SubAssetId round-trip through canonical UTF-8 metadata and asset URI normalization.",900,["changed","full","stress"]);
        AddSpecified(Stages,"content.importer-conformance","Importer conformance",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-025"],["Docs/Specifications/Asset-Content-Pipeline.md","src/CSharpGameEngine.Import/","assets/TestFixtures/"],"Each importer produces engine-owned intermediate data matching the fixture contract without leaking provider types.",900,["changed","full","stress"]);
        AddSpecified(Stages,"content.artifact-schema","Content artifact envelope",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-005"],["Docs/Specifications/Asset-Content-Pipeline.md","src/CSharpGameEngine.Core/"],"Little-endian header, schema, provenance, dependencies, content hash, and checksum reject corruption and preserve canonical bytes.",900,["changed","full","stress"]);
        AddSpecified(Stages,"content.incremental-cook","Incremental content compiler",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-026"],["Docs/Specifications/Asset-Content-Pipeline.md","tools/","src/CSharpGameEngine.Import/"],"The content DAG rebuilds only reachable dirty nodes, publishes atomically, and records deterministic dependency provenance.",900,["changed","full","stress"]);
        AddSpecified(Stages,"content.cache-recovery","Content cache recovery and GC",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-027"],["Docs/Specifications/Asset-Content-Pipeline.md","tools/"],"Interrupted publication retains the last-known-good artifact and reachability GC removes only unreachable CAS objects.",900,["full","stress"]);
        AddSpecified(Stages,"content.runtime-load","Asynchronous runtime asset handles",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-008"],["Docs/Specifications/Asset-Content-Pipeline.md","src/CSharpGameEngine.Runtime.Windows/"],"AssetHandle retain, release, cancellation, and asynchronous completion obey ownership and last-known-good rules.",900,["full","stress"]);
        AddSpecified(Stages,"content.package","Shipping content manifest",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-006"],["Docs/Specifications/Asset-Content-Pipeline.md","Docs/Specifications/Testing-CI-Release.md"],"The shipping manifest contains exactly the reachable artifact closure with hashes and provenance.",900,["full","stress"]);
        AddSpecified(Stages,"vsix.asset-authoring","VSIX asset authoring",FeatureStagePack.Vsix,FeatureStageMode.Visual,["F-L8-019"],["Docs/Specifications/Asset-Content-Pipeline.md","Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/"],"Asset metadata and import status display through the versioned host and invalid edits leave source unchanged.",1800,["full","vsix"]);
        AddSpecified(Stages,"content.performance","Content pipeline performance evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-028"],["Docs/Specifications/Asset-Content-Pipeline.md","tools/CSharpGameEngine.FeatureRunner/"],"Thirty post-warm-up incremental and cold-cache samples report median, p95, MAD, and machine fingerprint.",7200,["full","stress"]);
        AddSpecified(Stages,"content.release","Content pipeline release evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-029"],["Docs/Specifications/Asset-Content-Pipeline.md","Docs/Specifications/Testing-CI-Release.md"],"The scoped release closure, schema evidence, SBOM input, and installable package validate together.",7200,["full","stress"]);
    }
    private static void AddEditorVsixRoadmapStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"vsix.shader-graph","Visual Shader Graph editor",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-031"],[Test("tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj","FullyQualifiedName~ShaderGraph")],["Docs/Specifications/Shader-Graph.md","Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/","tests/CSharpGameEngine.VSExtension.Tests/"],"Editor Factory, built-in palette, exact typed connections, structured If/ForRange/Return authoring, native Undo/Redo, localization, save/build diagnostics, generated HLSL command, and target-selecting templates remain packaged in the VSIX.",1800,["changed","full","vsix"]);
        AddExecutable(Stages,"vsix.experimental-smoke","VSIX manual and Experimental Instance baseline",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-020"],[new FeatureStageCommand("dotnet",["run","--project","tools/CSharpGameEngine.Vsix.Automation/CSharpGameEngine.Vsix.Automation.csproj","--configuration","{configuration}","--","validate-ledger","--manifest","Docs/Verification/VSIX-Experimental-Baseline.json"],[],[],[2])],["Docs/Verification/VSIX-Experimental-Baseline.json","Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.Vsix.Automation/"],"The versioned ledger accepts explicit Pending entries but rejects false Passed entries whose evidence is missing, expired, or unobserved.",3600,["vsix"]);
        AddExecutable(Stages,"vsix.experimental-automation","VSIX Experimental Instance automation",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-021"],[new FeatureStageCommand("dotnet",["run","--project","tools/CSharpGameEngine.Vsix.Automation/CSharpGameEngine.Vsix.Automation.csproj","--configuration","{configuration}","--","run","--scenario","shader-graph","--configuration","{configuration}","--root-suffix","CgeAutomation","--timeout-seconds","3300","--evidence-dir",".artifacts/VsixExperimental","--locale","auto"],[],[],[2])],["Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/","tools/CSharpGameEngine.Vsix.Automation/","assets/Shaders/Triangle.cgeshadergraph"],"The dedicated CgeAutomation hive proves package provenance, Shader Graph editing, reload choices, diagnostics, ActivityLog identity, screenshots, and owned-process shutdown.",3600,["vsix"]);
        AddSpecified(Stages,"vsix.host-lifecycle","Versioned authoring host lifecycle",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-022"],["Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.EditorPreview.Host/","tools/CSharpGameEngine.VSExtension/"],"Preview, import, and bake protocol version, cancellation, timeout, crash, and mismatch transitions match the state machine.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.transform-tools","Transform tool visual conformance",FeatureStagePack.Vsix,FeatureStageMode.Visual,["F-L8-023"],["Docs/Specifications/Editor-VSIX.md","Docs/Specifications/Scene-Editor-Design.md","tools/CSharpGameEngine.VSExtension/"],"Move, Rotate, Scale, Snap, WARP display, rebase, and one-native-Undo behavior converge on reparsed C# source.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.inspector-contracts","Typed inspector and component palette",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-024"],["Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/"],"Enum, vector, asset reference, add, remove, and reorder edits preserve valid source and reject invalid input without mutation.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.source-sync","C# source synchronization v2",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-025"],["Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/"],"A predicted edit commits once, maps to one native Undo entry, and always converges to the Roslyn reparse result.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.bake-integration","Bake, asset, and diagnostic UI",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-026"],["Docs/Specifications/Editor-VSIX.md","Docs/Specifications/Asset-Content-Pipeline.md","tools/CSharpGameEngine.VSExtension/"],"Bake and import progress, failures, and actionable diagnostics are protocol-backed and cannot report a false success.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.debug-tools","Read-only runtime debug tools",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-027"],["Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/"],"Render graph, profiler, and debug viewers request immutable snapshots on demand and never mutate runtime state.",1800,["full","vsix"]);
        AddSpecified(Stages,"vsix.compatibility","VSIX compatibility matrix",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-028"],["Docs/Specifications/Editor-VSIX.md","Docs/Specifications/Testing-CI-Release.md"],"net472 VSIX build, supported Visual Studio version, and Experimental Instance evidence match the compatibility manifest.",3600,["vsix"]);
        AddSpecified(Stages,"vsix.release","VSIX release evidence",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-029"],["Docs/Specifications/Editor-VSIX.md","Docs/Specifications/Testing-CI-Release.md"],"The VSIX release payload has signed inputs, package provenance, installation, launch, and uninstall evidence.",7200,["vsix"]);
    }
    private static void AddEcsRuntimeRoadmapStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"ecs.baseline","ECS runtime baseline conformance",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-030"],[Test("tests/CSharpGameEngine.Entities.Tests/CSharpGameEngine.Entities.Tests.csproj"),Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj")],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Entities/","src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.Entities.Tests/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"Entities and CodeAnalysis suites establish closed-set archetype, deterministic scene lifecycle, system-major ordering, and delayed structural-change baseline.",900,["changed","full","stress"]);
        AddExecutable(Stages,"ecs.fixed-tick","Fixed tick scheduler conformance",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-018"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~FixedTickScheduler"),Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj","FullyQualifiedName~FixedTick")],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Runtime.Simulation/","src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.Windows.Tests/Simulation/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"At 60 Hz, pause, step, interpolation boundaries, retained backlog, and the maximum eight catch-up ticks follow the fixed state machine.",900,["full","stress"]);
        AddSpecified(Stages,"ecs.runtime-boundary","Portable shared Transform module",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L5-035"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Runtime.Windows/","src/CSharpGameEngine.Entities/"],"Rendering, physics, and audio share the portable Transform type without Windows namespace or platform dependencies.",900,["changed","full","stress"]);
        AddSpecified(Stages,"ecs.structural-changes","Unified structural command buffer",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-021"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Entities/"],"Create, Destroy, Add, Remove, and Move commands replay at tick end in stable system, partition, and sequence order.",900,["full","stress"]);
        AddSpecified(Stages,"ecs.change-tracking","Enable masks and change versions",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-022"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Entities/","src/CSharpGameEngine.CodeAnalysis/"],"Entity and component enable bits plus change-version queries produce generated deterministic results without runtime mask penalties for one to sixty-four types.",900,["full","stress"]);
        AddSpecified(Stages,"ecs.schedule-diagnostics","Generated schedule diagnostics",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-031"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.CodeAnalysis/"],"The source generator reports access conflicts and cycles before execution and merges worker commands and events in stable order.",900,["full","stress"]);
        AddSpecified(Stages,"ecs.snapshot","Schema-fingerprinted ECS snapshots",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-007"],["Docs/Specifications/Ecs-Runtime.md","src/CSharpGameEngine.Entities/"],"Snapshot schema fingerprints, checksums, and deterministic payloads reject unknown schema and corruption without an old-reader path.",900,["full","stress"]);
        AddSpecified(Stages,"ecs.performance","ECS runtime performance evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-032"],["Docs/Specifications/Ecs-Runtime.md","tools/CSharpGameEngine.Ecs.Benchmarks/"],"Thirty post-warm-up samples prove the generated one-to-sixty-four-type path retains no runtime mask penalty.",7200,["full","stress"]);
        AddSpecified(Stages,"ecs.release","ECS runtime release evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-033"],["Docs/Specifications/Ecs-Runtime.md","Docs/Specifications/Testing-CI-Release.md"],"The ECS release scope records schema, deterministic, performance, and generated-code closure evidence.",7200,["full","stress"]);
    }
    private static void AddAudioInputPlatformRoadmapStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"platform.boundaries","Platform event and timestamp contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L1-008"],[Test("tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj","FullyQualifiedName~InputContractsTests")],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Core/InputContracts.cs","tests/CSharpGameEngine.Core.Tests/InputContractsTests.cs"],"Portable timestamp, window, device, action, and audio value contracts are deterministic and contain no Win32 or XAudio2 types.",900,["changed","full","stress"]);
        AddExecutable(Stages,"platform.window-input","Win32 window and input binding",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L0-010"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Win32InputEventAdapterTests")],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Windows/Platform/Win32Window.cs","src/CSharpGameEngine.Windows/Platform/Win32InputEventAdapter.cs","tests/CSharpGameEngine.Windows.Tests/Input/Win32InputEventAdapterTests.cs"],"QPC-derived timestamps are monotonic and focus loss, capture release, and hotplug release all held input state.",900,["changed","full","stress"]);
        AddSpecified(Stages,"audio.lifecycle","Audio device lifecycle server",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-009"],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Windows/","src/CSharpGameEngine.Runtime.Windows/"],"Audio device creation, silent fallback, disposal, and recreation follow the declared ownership state machine.",900,["full","stress"]);
        AddExecutable(Stages,"input.actions","Deterministic input action server",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-010"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~InputServerTests")],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Windows/Input/InputServer.cs","tests/CSharpGameEngine.Windows.Tests/Input/InputServerTests.cs"],"Raw events produce deterministic action sequences across context, chord, dead-zone, and rebind cases.",900,["changed","full","stress"]);
        AddSpecified(Stages,"audio.mixer","Audio mixer and offline oracle",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-011"],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Windows/"],"The silent/offline backend validates PCM mix, buses, limits, looping, and underrun behavior without hardware audio.",900,["full","stress"]);
        AddSpecified(Stages,"audio.spatial","Spatial audio and recovery",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-012"],["Docs/Specifications/Audio-Input-Platform.md","src/CSharpGameEngine.Runtime.Windows/"],"Spatial voice state, stream artifacts, and device recreation restore or stop voices according to the declared recovery policy.",900,["full","stress"]);
        AddSpecified(Stages,"vsix.platform-debug","Platform diagnostics viewer",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-030"],["Docs/Specifications/Audio-Input-Platform.md","Docs/Specifications/Editor-VSIX.md","tools/CSharpGameEngine.VSExtension/"],"Platform, input, and audio diagnostics display immutable snapshots and never require a hardware device for headless evidence.",1800,["full","vsix"]);
        AddSpecified(Stages,"platform.compatibility","Platform compatibility evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-034"],["Docs/Specifications/Audio-Input-Platform.md","Docs/Specifications/Testing-CI-Release.md"],"Windows x64 Debug and Release evidence separates mandatory silent/WARP checks from optional hardware audio and GPU checks.",3600,["full","stress"]);
        AddSpecified(Stages,"platform.release","Platform release evidence",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-035"],["Docs/Specifications/Audio-Input-Platform.md","Docs/Specifications/Testing-CI-Release.md"],"Platform release scope records adapter, device, timestamp, input, and audio provenance with the required dependency closure.",7200,["full","stress"]);
    }
    private static void AddTestingCiReleaseRoadmapStages(List<FeatureStageDefinition>Stages){
        AddSpecified(Stages,"verification.coverage","Feature coverage and catalog validation",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-036"],["Docs/Specifications/Testing-CI-Release.md","Docs/Implementation-Status.md","tools/CSharpGameEngine.FeatureRunner/"],"Ledger, catalog, status, stage assignment, track, Level, dependency, and Visual Scene bindings agree exactly.",900,["changed","full","stress"]);
        AddSpecified(Stages,"verification.manual-ledger","Manual verification ledger",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-037"],["Docs/Specifications/Testing-CI-Release.md","Docs/Implementation-Status.md"],"Manual checks have an owner, evidence link, expiry, and explicit pending state; missing evidence cannot pass a release.",900,["full","stress"]);
        AddSpecified(Stages,"verification.changed-selection","Changed-path stage selection",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-038"],["Docs/Specifications/Testing-CI-Release.md","tools/CSharpGameEngine.FeatureRunner/"],"Changed-path selection includes executable affected stages, excludes NotImplemented stages, and falls back only to runner.contract.",900,["changed","full","stress"]);
        AddSpecified(Stages,"verification.clean-build","Hermetic clean-build verification",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-039"],["Docs/Specifications/Testing-CI-Release.md","Directory.Build.props","global.json"],"Clean restore, Debug and Release build, test, and artifact checks use no stale output or undeclared local dependency.",900,["full","stress"]);
        AddSpecified(Stages,"verification.golden-policy","Golden artifact policy",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-040"],["Docs/Specifications/Testing-CI-Release.md","assets/"],"Golden updates require provenance, reviewable diff, deterministic regeneration, and no silent tolerance widening.",900,["full","stress"]);
        AddSpecified(Stages,"verification.performance","Performance measurement framework",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-041"],["Docs/Specifications/Testing-CI-Release.md","tools/CSharpGameEngine.FeatureRunner/"],"Five warm-ups and thirty samples record median, p95, MAD, and machine fingerprint in a versioned result.",7200,["full","stress"]);
        AddSpecified(Stages,"verification.ci-matrix","Explicit CI matrix",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-042"],["Docs/Specifications/Testing-CI-Release.md",".github/"],"Windows x64 Debug/Release, net10.0, Windows TFM, and net472 VSIX are explicit required cells; WARP is mandatory.",7200,["full","stress"]);
        AddSpecified(Stages,"verification.failure-policy","Failure, retry, and quarantine policy",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-043"],["Docs/Specifications/Testing-CI-Release.md",".github/"],"Only InfrastructureError retries once; quarantine has an owner and a maximum fourteen-day expiry, and timeout never becomes success by retry.",900,["full","stress"]);
        AddSpecified(Stages,"verification.stress-contract","Stress contract",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-044"],["Docs/Specifications/Testing-CI-Release.md","tools/CSharpGameEngine.FeatureRunner/"],"Stress evidence runs under the declared 28,800-second budget and records deterministic failure reproduction data.",28800,["stress"]);
        AddSpecified(Stages,"release.packages","Package install and SBOM validation",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-045"],["Docs/Specifications/Testing-CI-Release.md","Docs/Specifications/Asset-Content-Pipeline.md"],"Dependency closure, SPDX JSON SBOM, signature inputs, install, run, and uninstall validate from the release package.",7200,["full","stress"]);
        AddSpecified(Stages,"release.compatibility","Release compatibility matrix",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-046"],["Docs/Specifications/Testing-CI-Release.md"],"The required Windows, runtime, VSIX, WARP, and optional-hardware compatibility cells are recorded by release scope.",7200,["full","stress"]);
        AddSpecified(Stages,"release.candidate","Release candidate gate",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-047"],["Docs/Specifications/Testing-CI-Release.md"],"Only required executable stages in the target tracks and reached Levels may gate an RC; Specified stages remain visible but cannot be counted as passed.",7200,["full","stress"]);
        AddSpecified(Stages,"release.manifest","Immutable release manifest",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-048"],["Docs/Specifications/Testing-CI-Release.md"],"The final manifest, packages, hashes, provenance, SBOM, and retention class are immutable after formal release.",7200,["full","stress"]);
    }
    private static void AddNonPhysicsRoadmapStages(List<FeatureStageDefinition>Stages){
        AddRenderingRoadmapStages(Stages);
        AddAssetContentRoadmapStages(Stages);
        AddEditorVsixRoadmapStages(Stages);
        AddEcsRuntimeRoadmapStages(Stages);
        AddAudioInputPlatformRoadmapStages(Stages);
        AddTestingCiReleaseRoadmapStages(Stages);
    }
    private static void AddDeferredSimulationStages(List<FeatureStageDefinition>Stages){
        AddDeferred(Stages,"rigid.joints","Joint constraints",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-004"],"Pendulum and locked-joint constraint drift remain within the deterministic error budget.");
        AddDeferred(Stages,"rigid.ragdoll","Ragdoll",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-005"],"Pose handoff, joint limits, and final state hash match the reference run.");
        AddDeferred(Stages,"rigid.character-controller","Character controller",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-006"],"Sweep movement traverses the reference stair and slope course without penetration.");
        AddDeferred(Stages,"rigid.vehicle","Vehicle",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-007"],"Reference acceleration, braking, suspension travel, and lap state remain within tolerance.");
        AddDeferred(Stages,"rigid.dynamic-ccd","Dynamic-dynamic CCD",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-008"],"Opposing high-speed bodies produce a TOI contact without tunneling.");
        AddDeferred(Stages,"rigid.parallel-solver","Parallel rigid solver",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-009"],"Graph-colored execution matches the single-thread state and event hashes.");
        AddDeferred(Stages,"rigid.gpu-solver","GPU rigid solver",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-010"],"WARP compute output matches the CPU reference within the backend-specific tolerance.");
        AddDeferred(Stages,"rigid.rollback","Physics rollback",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-011"],"Snapshot restore and command replay reproduce the original state and event hashes.");
        AddDeferred(Stages,"deformable.xpbd","PBD and XPBD",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-012"],"Distance-constraint convergence and energy behavior match the reference vectors.");
        AddDeferred(Stages,"deformable.cloth","Cloth",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-013"],"Pinned cloth settles without inversion and matches reference vertex tolerances.");
        AddDeferred(Stages,"deformable.rope","Rope",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-014"],"Endpoint constraints, segment lengths, and collision events remain within tolerance.");
        AddDeferred(Stages,"deformable.soft-body","Soft body",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-015"],"Volume preservation and collision recovery match the selected backend reference.");
        AddDeferred(Stages,"deformable.fem","Finite element method",FeatureStagePack.Deformable,FeatureStageMode.Headless,["F-L5-016"],"Canonical beam displacement and stress stay within documented discretization error.");
        AddDeferred(Stages,"deformable.mpm","Material point method",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-017"],"Mass and momentum budgets close for the reference transfer cycle.");
        AddDeferred(Stages,"fluid.sph-pbf","SPH and PBF",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-018"],"Density error, mass conservation, and boundary impulse remain within tolerance.");
        AddDeferred(Stages,"fluid.flip-apic","FLIP and APIC",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-019"],"Particle-grid transfer preserves mass and bounded kinetic energy.");
        AddDeferred(Stages,"fluid.eulerian-grid","Eulerian grid fluid",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-020"],"Projection reduces divergence and conserves scalar mass within tolerance.");
        AddDeferred(Stages,"destruction.runtime-topology","Runtime Voronoi and Boolean destruction",FeatureStagePack.Destruction,FeatureStageMode.Visual,["F-L5-021"],"Fragments form a closed deterministic partition with stable IDs and valid cooked collision.");
        AddDeferred(Stages,"destruction.voxel-sdf","Voxel and SDF destruction",FeatureStagePack.Destruction,FeatureStageMode.Visual,["F-L5-022"],"Reference cut updates only affected chunks and extracted surface hashes remain deterministic.");
        AddDeferred(Stages,"celestial.kepler","Kepler orbit",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-023"],"One-period orbit closes within the declared position and energy error budgets.");
        AddDeferred(Stages,"celestial.direct-nbody","Direct N-body",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-024"],"Two-body and three-body references remain within energy and momentum budgets.");
        AddDeferred(Stages,"celestial.barnes-hut","Barnes-Hut gravity",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-025"],"Approximate forces stay within theta-dependent error against direct N-body.");
        AddDeferred(Stages,"celestial.fmm","Fast multipole gravity",FeatureStagePack.Celestial,FeatureStageMode.Headless,["F-L5-026"],"Multipole field samples stay within order-dependent error against direct N-body.");
        AddDeferred(Stages,"celestial.hierarchical-frames","Hierarchical celestial coordinates",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-027"],"Round-trip frame transforms and rebasing preserve declared precision at every scale.");
        AddDeferred(Stages,"probability.gameplay","Gameplay probability domain",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Visual,["F-L5-028"],"Seeded discrete observations reproduce the golden event sequence and distribution checks.");
        AddDeferred(Stages,"probability.wavefunction-grid","Wavefunction grid",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Visual,["F-L5-029"],"Norm, phase evolution, and analytic reference error stay within the declared budget.");
        AddDeferred(Stages,"probability.monte-carlo-quantum","Monte Carlo scientific quantum domain",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Headless,["F-L5-030"],"Seeded estimates report provenance and confidence intervals covering the analytic reference.");
        AddDeferred(Stages,"coupling.multidomain","Cross-domain coupling",FeatureStagePack.Coupling,FeatureStageMode.Visual,["F-L5-031"],"One-tick bridges preserve ownership, ordering, units, and declared conservation budgets.");
    }
    private static void Add(List<FeatureDefinition>Features,string Id,string Name,string Layer,FeatureLifecycleStatus Status)=>Add(Features,Id,Name,Layer,Status,FeatureRoadmapTrack.Established,0,[]);
    private static void Add(List<FeatureDefinition>Features,string Id,string Name,string Layer,FeatureLifecycleStatus Status,FeatureRoadmapTrack Track,int DependencyLevel,IReadOnlyList<string>DependsOnFeatureIds)=>Features.Add(new FeatureDefinition(Id,Name,Layer,Status,Track,DependencyLevel,DependsOnFeatureIds));
    private static FeatureStageCommand Test(string Project,string?Filter=null){
        var Arguments=new List<string>{"test",Project,"--configuration","{configuration}","--nologo"};
        if(!string.IsNullOrWhiteSpace(Filter)){ Arguments.AddRange(["--filter",Filter]); }
        return new FeatureStageCommand("dotnet",Arguments);
    }
    private static FeatureStageCommand Scene(string Project,string Id)=>new("dotnet",["run","--project",Project,"--configuration","{configuration}","--","run",Id]);
    private static void AddExecutable(List<FeatureStageDefinition>Stages,string Id,string Name,FeatureStagePack Pack,FeatureStageMode Mode,IReadOnlyList<string>FeatureIds,IReadOnlyList<FeatureStageCommand>Commands,IReadOnlyList<string>InputPaths,string Oracle,int TimeoutSeconds,IReadOnlyList<string>Profiles,FeatureAdapterRequirement AdapterRequirement=FeatureAdapterRequirement.None)=>Stages.Add(new FeatureStageDefinition(Id,Name,Pack,Mode,FeatureStageAvailability.Executable,AdapterRequirement,FeatureIds,Profiles,5489,600,TimeoutSeconds,Oracle,Commands,InputPaths,SceneProject(Mode,Pack),SceneId(Mode,Id)));
    private static void AddSpecified(List<FeatureStageDefinition>Stages,string Id,string Name,FeatureStagePack Pack,FeatureStageMode Mode,IReadOnlyList<string>FeatureIds,IReadOnlyList<string>InputPaths,string Oracle,int TimeoutSeconds,IReadOnlyList<string>Profiles)=>Stages.Add(new FeatureStageDefinition(Id,Name,Pack,Mode,FeatureStageAvailability.NotImplemented,Mode==FeatureStageMode.Visual?FeatureAdapterRequirement.WarpRequiredHardwareOptional:FeatureAdapterRequirement.None,FeatureIds,Profiles,5489,600,TimeoutSeconds,Oracle,[],InputPaths,SceneProject(Mode,Pack),SceneId(Mode,Id)));
    private static void AddDeferred(List<FeatureStageDefinition>Stages,string Id,string Name,FeatureStagePack Pack,FeatureStageMode Mode,IReadOnlyList<string>FeatureIds,string Oracle)=>Stages.Add(new FeatureStageDefinition(Id,Name,Pack,Mode,FeatureStageAvailability.NotImplemented,Mode==FeatureStageMode.Visual?FeatureAdapterRequirement.WarpRequiredHardwareOptional:FeatureAdapterRequirement.None,FeatureIds,["full","stress"],5489,600,1800,Oracle,[],[],SceneProject(Mode,Pack),SceneId(Mode,Id)));
    private static string?SceneProject(FeatureStageMode Mode,FeatureStagePack Pack)=>Mode==FeatureStageMode.Visual?$"stages/CSharpGameEngine.FeatureStages.{Pack}/CSharpGameEngine.FeatureStages.{Pack}.csproj":null;
    private static string?SceneId(FeatureStageMode Mode,string Id)=>Mode==FeatureStageMode.Visual?Id:null;
}
