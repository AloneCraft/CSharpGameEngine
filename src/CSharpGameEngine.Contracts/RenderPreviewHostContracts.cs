namespace CSharpGameEngine.Contracts;

/// <summary>
/// Versioned, provider-neutral wire contract between an L8 preview client and the isolated
/// Render Server host. Only presentation-relative <see cref="float"/> values cross this boundary;
/// native graphics objects and engine-owned authoritative state never do.
/// </summary>
public static class RenderPreviewHostProtocol {
    public const int CurrentVersion=6;
    public const int CurrentDebugSnapshotVersion=2;
    public const int MaximumDimension=16_384;
    public const int MaximumMeshCount=4_096;
    public const int MaximumMaterialCount=4_096;
    public const int MaximumObjectCount=100_000;
    public const int MaximumOverlayVertexCount=32_768;
    public const int MaximumCameraPreviewCount=3;
    public const int MaximumDebugPassCount=64;
    public const int MaximumDebugResourceCount=64;
    public const int MaximumDebugTransitionCount=256;
    public const int MaximumDebugDiagnosticCount=64;
    public const int MaximumDebugNameLength=128;
    public const int MaximumDebugDiagnosticDescriptionLength=1_024;
    public const int MaximumPlatformDebugTextLength=1_024;
    public const int MaximumPlatformDebugInputActionCount=64;

    public static string?Validate(RenderPreviewHostRequest Request){
        if(Request is null){ return "Request is required."; }
        if(Request.ProtocolVersion!=CurrentVersion){
            return $"Unsupported render preview host protocol {Request.ProtocolVersion}; expected {CurrentVersion}.";
        }
        if(Request.RequestId==Guid.Empty){ return "RequestId is required."; }
        if(Request.SessionId==Guid.Empty){ return "SessionId is required."; }
        if(Request.Sequence==0){ return "Sequence must be greater than zero."; }
        if(!Enum.IsDefined(typeof(RenderPreviewHostCommand),Request.Command)){
            return "Render preview host command is unknown.";
        }
        if(Request.RuntimeView is not null&&Request.Command!=RenderPreviewHostCommand.Open){
            return "Only Open may request a runtime view.";
        }
        if(Request.SceneJson.Length>0&&Request.Command is not (RenderPreviewHostCommand.Open or RenderPreviewHostCommand.ReplaceScene)){
            return "Only Open and ReplaceScene carry a scene document.";
        }

        return Request.Command switch {
            RenderPreviewHostCommand.Open=>ValidateOpen(Request),
            RenderPreviewHostCommand.ReplaceScene=>ValidateReplaceScene(Request),
            RenderPreviewHostCommand.UpdateFrame=>ValidateUpdateFrame(Request),
            RenderPreviewHostCommand.Resize=>ValidateResize(Request),
            RenderPreviewHostCommand.Render=>ValidateRender(Request),
            RenderPreviewHostCommand.Close=>ValidateEmptyCommand(Request),
            RenderPreviewHostCommand.Snapshot=>ValidateEmptyCommand(Request),
            _=>"Render preview host command is unknown.",
        };
    }

    public static string?ValidateDebugSnapshot(RenderPreviewDebugSnapshot?Snapshot){
        if(Snapshot is null){ return "Render debug snapshot is required."; }
        if(Snapshot.SnapshotVersion!=CurrentDebugSnapshotVersion){
            return $"Unsupported render debug snapshot {Snapshot.SnapshotVersion}; expected {CurrentDebugSnapshotVersion}.";
        }
        if(!Enum.IsDefined(typeof(RenderPreviewDebugGraphKind),Snapshot.GraphKind)){
            return "Render debug graph kind is unknown.";
        }
        if(!Enum.IsDefined(typeof(RenderPreviewDebugDeviceState),Snapshot.DeviceState)){
            return "Render debug device state is unknown.";
        }
        if(!ValidPositiveDimensions(Snapshot.SurfaceWidth,Snapshot.SurfaceHeight)){
            return $"Render debug surface dimensions must both be between 1 and {MaximumDimension}.";
        }
        if(Snapshot.SceneObjectCount<0||Snapshot.SceneObjectCount>MaximumObjectCount
            ||Snapshot.PrimaryObjectDrawCount<0
            ||Snapshot.PrimaryObjectDrawCount>Snapshot.SceneObjectCount){
            return "Render debug object counters are outside their supported range.";
        }
        if(Snapshot.ResidentMaterialTextureCount<0
            ||Snapshot.ResidentMaterialTextureCount>MaximumMaterialCount
            ||Snapshot.ErrorOrWorseMessageCount<0){
            return "Render debug resource or error counters are outside their supported range.";
        }
        string?Error=ValidateDebugName(Snapshot.AdapterDescription,"adapter description");
        if(Error is not null){ return Error; }
        Error=ValidateDebugName(Snapshot.QualityProfile,"quality profile");
        if(Error is not null){ return Error; }
        if(Snapshot.Passes is null||Snapshot.Passes.Length is<1 or>MaximumDebugPassCount){
            return $"Render debug pass count must be between 1 and {MaximumDebugPassCount}.";
        }
        if(Snapshot.Resources is null||Snapshot.Resources.Length is<1 or>MaximumDebugResourceCount){
            return $"Render debug resource count must be between 1 and {MaximumDebugResourceCount}.";
        }
        if(Snapshot.Transitions is null
            ||Snapshot.Transitions.Length>MaximumDebugTransitionCount){
            return $"Render debug transition count must be between 0 and {MaximumDebugTransitionCount}.";
        }
        if(Snapshot.Diagnostics is null
            ||Snapshot.Diagnostics.Length>MaximumDebugDiagnosticCount){
            return $"Render debug diagnostic count must be between 0 and {MaximumDebugDiagnosticCount}.";
        }
        if(Snapshot.DiagnosticsTruncated
            &&Snapshot.Diagnostics.Length!=MaximumDebugDiagnosticCount){
            return "A truncated render debug diagnostic set must fill its bounded capacity.";
        }
        Error=ValidatePlatformDebugSnapshot(Snapshot.Platform);
        if(Error is not null){ return Error; }

        bool[]PassIndices=new bool[Snapshot.Passes.Length];
        bool[]ExecutionIndices=new bool[Snapshot.Passes.Length];
        for(int Position=0;Position<Snapshot.Passes.Length;Position++){
            RenderPreviewDebugPass?Pass=Snapshot.Passes[Position];
            if(Pass is null){ return $"Render debug pass {Position} is null."; }
            if((uint)Pass.Index>=(uint)PassIndices.Length||PassIndices[Pass.Index]){
                return $"Render debug pass {Position} has an invalid or duplicated index.";
            }
            if((uint)Pass.ExecutionIndex>=(uint)ExecutionIndices.Length
                ||ExecutionIndices[Pass.ExecutionIndex]){
                return $"Render debug pass {Position} has an invalid or duplicated execution index.";
            }
            PassIndices[Pass.Index]=true;
            ExecutionIndices[Pass.ExecutionIndex]=true;
            Error=ValidateDebugName(Pass.Name,$"pass {Position} name");
            if(Error is not null){ return Error; }
            if(!Enum.IsDefined(typeof(RenderPreviewDebugQueue),Pass.Queue)){
                return $"Render debug pass {Position} queue is unknown.";
            }
            if(Pass.Dependencies is null||Pass.Dependencies.Length>Snapshot.Passes.Length){
                return $"Render debug pass {Position} dependencies are invalid.";
            }
            HashSet<int>Dependencies=[];
            foreach(int Dependency in Pass.Dependencies){
                if((uint)Dependency>=(uint)Snapshot.Passes.Length
                    ||Dependency==Pass.Index||!Dependencies.Add(Dependency)){
                    return $"Render debug pass {Position} has an invalid dependency.";
                }
            }
        }

        bool[]ResourceIndices=new bool[Snapshot.Resources.Length];
        for(int Position=0;Position<Snapshot.Resources.Length;Position++){
            RenderPreviewDebugResource?Resource=Snapshot.Resources[Position];
            if(Resource is null){ return $"Render debug resource {Position} is null."; }
            if((uint)Resource.Index>=(uint)ResourceIndices.Length
                ||ResourceIndices[Resource.Index]){
                return $"Render debug resource {Position} has an invalid or duplicated index.";
            }
            ResourceIndices[Resource.Index]=true;
            Error=ValidateDebugName(Resource.Name,$"resource {Position} name");
            if(Error is not null){ return Error; }
            if(!Enum.IsDefined(typeof(RenderPreviewDebugResourceKind),Resource.Kind)){
                return $"Render debug resource {Position} kind is unknown.";
            }
            bool InvalidDescriptor=Resource.Kind switch {
                RenderPreviewDebugResourceKind.Buffer=>Resource.ByteSize==0
                    ||Resource.Width!=0||Resource.Height!=0
                    ||!string.IsNullOrEmpty(Resource.Format),
                RenderPreviewDebugResourceKind.Texture=>Resource.ByteSize!=0
                    ||!ValidPositiveDimensions(Resource.Width,Resource.Height)
                    ||string.IsNullOrWhiteSpace(Resource.Format)
                    ||Resource.Format is null
                    ||Resource.Format.Length>MaximumDebugNameLength,
                _=>true,
            };
            if(InvalidDescriptor){
                return $"Render debug resource {Position} descriptor is invalid.";
            }
            if(Resource.FirstUse<0||Resource.LastUse<Resource.FirstUse
                ||Resource.LastUse>=Snapshot.Passes.Length
                ||(Resource.Imported?Resource.AliasSlot!=-1:Resource.AliasSlot<0)){
                return $"Render debug resource {Position} lifetime is invalid.";
            }
        }

        for(int Position=0;Position<Snapshot.Transitions.Length;Position++){
            RenderPreviewDebugTransition?Transition=Snapshot.Transitions[Position];
            if(Transition is null){ return $"Render debug transition {Position} is null."; }
            if((uint)Transition.ResourceIndex>=(uint)Snapshot.Resources.Length
                ||(uint)Transition.PassIndex>=(uint)Snapshot.Passes.Length
                ||!Enum.IsDefined(typeof(RenderPreviewDebugResourceState),Transition.Before)
                ||!Enum.IsDefined(typeof(RenderPreviewDebugResourceState),Transition.After)
                ||!Enum.IsDefined(typeof(RenderPreviewDebugQueue),Transition.SourceQueue)
                ||!Enum.IsDefined(typeof(RenderPreviewDebugQueue),Transition.DestinationQueue)){
                return $"Render debug transition {Position} is invalid.";
            }
        }
        for(int Position=0;Position<Snapshot.Diagnostics.Length;Position++){
            RenderPreviewDebugDiagnostic?Diagnostic=Snapshot.Diagnostics[Position];
            if(Diagnostic is null){ return $"Render debug diagnostic {Position} is null."; }
            if(!Enum.IsDefined(typeof(RenderPreviewDebugSeverity),Diagnostic.Severity)){
                return $"Render debug diagnostic {Position} severity is unknown.";
            }
            Error=ValidateDebugName(Diagnostic.Category,$"diagnostic {Position} category");
            if(Error is not null){ return Error; }
            Error=ValidateDebugName(Diagnostic.Id,$"diagnostic {Position} id");
            if(Error is not null){ return Error; }
            if(string.IsNullOrWhiteSpace(Diagnostic.Description)
                ||Diagnostic.Description.Length>MaximumDebugDiagnosticDescriptionLength){
                return $"Render debug diagnostic {Position} description is invalid.";
            }
        }
        return null;
    }

    public static string?ValidatePlatformDebugSnapshot(PlatformDebugSnapshot?Snapshot){
        if(Snapshot is null){ return "Platform debug snapshot is required."; }
        if(Snapshot.CapturedCounter<0||Snapshot.CounterFrequency<=0){
            return "Platform debug monotonic counter values are invalid.";
        }
        string?Error=ValidatePlatformText(Snapshot.OsDescription,"OS description");
        if(Error is not null){ return Error; }
        Error=ValidatePlatformText(Snapshot.OsArchitecture,"OS architecture");
        if(Error is not null){ return Error; }
        Error=ValidatePlatformText(Snapshot.ProcessArchitecture,"process architecture");
        if(Error is not null){ return Error; }
        Error=ValidatePlatformText(Snapshot.RuntimeDescription,"runtime description");
        if(Error is not null){ return Error; }
        if(Snapshot.ProcessId<=0){ return "Platform debug process ID must be positive."; }
        Error=ValidateInputDebugSnapshot(Snapshot.Input);
        if(Error is not null){ return Error; }
        return ValidateAudioDebugSnapshot(Snapshot.Audio);
    }

    private static string?ValidateInputDebugSnapshot(PlatformDebugInputSnapshot?Snapshot){
        if(Snapshot is null){ return "Platform input debug snapshot is required."; }
        if(!Enum.IsDefined(typeof(PlatformDebugSubsystemState),Snapshot.State)){
            return "Platform input debug state is unknown.";
        }
        if(Snapshot.ActiveContextCount<0||Snapshot.HeldControlCount<0
            ||Snapshot.ActiveActionCount<0){
            return "Platform input debug counters cannot be negative.";
        }
        if(Snapshot.Actions is null
            ||Snapshot.Actions.Length>MaximumPlatformDebugInputActionCount){
            return $"Platform input debug action count must be between 0 and {MaximumPlatformDebugInputActionCount}.";
        }
        if(Snapshot.State==PlatformDebugSubsystemState.NotOwned
            &&(Snapshot.ActiveContextCount!=0||Snapshot.HeldControlCount!=0
                ||Snapshot.ActiveActionCount!=0||Snapshot.Actions.Length!=0
                ||Snapshot.ActionsTruncated)){
            return "A not-owned platform input snapshot cannot report owned state.";
        }
        if(Snapshot.ActionsTruncated
            &&Snapshot.Actions.Length!=MaximumPlatformDebugInputActionCount){
            return "A truncated platform input action set must fill its bounded capacity.";
        }
        string?Error=ValidateOptionalPlatformText(Snapshot.Detail,"input detail");
        if(Error is not null){ return Error; }
        for(int Index=0;Index<Snapshot.Actions.Length;Index++){
            PlatformDebugInputAction?Action=Snapshot.Actions[Index];
            if(Action is null){ return $"Platform input debug action {Index} is null."; }
            if(Action.TimestampTicks<0||!Enum.IsDefined(
                typeof(PlatformDebugInputActionPhase),Action.Phase)
                ||!Enum.IsDefined(typeof(PlatformDebugInputReleaseReason),Action.ReleaseReason)
                ||!IsFinite(Action.RawX)||!IsFinite(Action.RawY)
                ||!IsFinite(Action.ValueX)||!IsFinite(Action.ValueY)){
                return $"Platform input debug action {Index} contains an invalid value.";
            }
            foreach((string Value,string Name) in new[]{
                (Action.WindowId,"window ID"),(Action.DeviceId,"device ID"),
                (Action.ContextId,"context ID"),(Action.ActionId,"action ID")}){
                Error=ValidatePlatformText(Value,$"input action {Index} {Name}");
                if(Error is not null){ return Error; }
            }
        }
        return null;
    }

    private static string?ValidateAudioDebugSnapshot(PlatformDebugAudioSnapshot?Snapshot){
        if(Snapshot is null){ return "Platform audio debug snapshot is required."; }
        if(!Enum.IsDefined(typeof(PlatformDebugSubsystemState),Snapshot.State)
            ||!Enum.IsDefined(typeof(PlatformDebugAudioBackendMode),Snapshot.Mode)){
            return "Platform audio debug state or backend mode is unknown.";
        }
        if(Snapshot.ActiveVoiceCount<0||Snapshot.MaximumVoiceCount<0
            ||Snapshot.ActiveVoiceCount>Snapshot.MaximumVoiceCount
            ||Snapshot.BusCount<0||Snapshot.RestoredVoiceCount<0
            ||Snapshot.StoppedVoiceCount<0||Snapshot.RecoveryEventCount<0
            ||Snapshot.DroppedRecoveryEventCount<0){
            return "Platform audio debug counters are outside their supported range.";
        }
        string?Error=ValidateOptionalPlatformText(Snapshot.LastFailure,"audio failure");
        if(Error is not null){ return Error; }
        Error=ValidateOptionalPlatformText(Snapshot.Detail,"audio detail");
        if(Error is not null){ return Error; }
        if(Snapshot.State==PlatformDebugSubsystemState.NotOwned){
            if(Snapshot.Mode!=PlatformDebugAudioBackendMode.NotOwned
                ||Snapshot.Generation!=0||Snapshot.ActiveVoiceCount!=0
                ||Snapshot.MaximumVoiceCount!=0||Snapshot.BusCount!=0
                ||Snapshot.RestoredVoiceCount!=0||Snapshot.StoppedVoiceCount!=0
                ||Snapshot.RecoveryEventCount!=0
                ||Snapshot.DroppedRecoveryEventCount!=0
                ||Snapshot.LastFailure is not null){
                return "A not-owned platform audio snapshot cannot report owned state.";
            }
            return null;
        }
        if(Snapshot.Mode==PlatformDebugAudioBackendMode.NotOwned){
            return "An owned platform audio snapshot cannot use the not-owned backend mode.";
        }
        if(Snapshot.State==PlatformDebugSubsystemState.Ready
            &&(Snapshot.Mode is not PlatformDebugAudioBackendMode.XAudio2
                and not PlatformDebugAudioBackendMode.Silent
                ||Snapshot.Generation==0)){
            return "A ready platform audio snapshot requires an active backend generation.";
        }
        return null;
    }

    private static string?ValidatePlatformText(string?Value,string Name)=>
        Value is null||string.IsNullOrWhiteSpace(Value)
            ||Value.Length>MaximumPlatformDebugTextLength
            ?$"Platform debug {Name} is invalid."
            :null;
    private static string?ValidateOptionalPlatformText(string?Value,string Name)=>
        Value is not null&&Value.Length>MaximumPlatformDebugTextLength
            ?$"Platform debug {Name} is too long."
            :null;

    private static string?ValidateOpen(RenderPreviewHostRequest Request){
        if(Request.SurfaceHandle==0){ return "Open requires a surface handle."; }
        if(!ValidPositiveDimensions(Request.Width,Request.Height)){
            return $"Open dimensions must both be between 1 and {MaximumDimension}.";
        }
        if(string.IsNullOrWhiteSpace(Request.ShaderArtifactPath)){ return "Open requires a shader artifact path."; }
        if(Request.Scene is null){ return "Open requires a scene."; }
        if(Request.RuntimeView is { }RuntimeView){
            if(string.IsNullOrWhiteSpace(RuntimeView.AssemblyPath)
                ||!RuntimeView.AssemblyPath.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){
                return "A runtime view requires the game assembly path.";
            }
            if(string.IsNullOrWhiteSpace(RuntimeView.SceneName)){ return "A runtime view requires the scene name."; }
        }
        return ValidateScene(Request.Scene);
    }

    private static string?ValidateReplaceScene(RenderPreviewHostRequest Request){
        if(Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0||!string.IsNullOrEmpty(Request.ShaderArtifactPath)
            ||Request.Frame is not null||Request.CaptureForVerification){
            return "ReplaceScene must not include surface, dimensions, or a shader artifact path.";
        }
        if(Request.Scene is null){ return "ReplaceScene requires a scene."; }
        return ValidateScene(Request.Scene);
    }

    private static string?ValidateResize(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return "Resize must not include a scene, surface, or shader artifact path.";
        }
        bool Suspended=Request.Width==0&&Request.Height==0;
        if(!Suspended&&!ValidPositiveDimensions(Request.Width,Request.Height)){
            return $"Resize dimensions must both be zero or both be between 1 and {MaximumDimension}.";
        }
        return null;
    }

    private static string?ValidateEmptyCommand(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return $"{Request.Command} must not include scene, surface, dimensions, or a shader artifact path.";
        }
        return null;
    }

    private static string?ValidateRender(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)){
            return "Render must not include scene, surface, dimensions, or a shader artifact path.";
        }
        return null;
    }

    private static string?ValidateUpdateFrame(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return "UpdateFrame must not include scene, surface, dimensions, shader artifact, or capture.";
        }
        if(Request.Frame?.Camera is null){ return "UpdateFrame requires a camera."; }
        string?Error=ValidateCamera(Request.Frame.Camera,"Frame camera");
        if(Error is not null){ return Error; }
        if(Request.Frame.Objects is null||Request.Frame.Objects.Length>MaximumObjectCount){
            return $"Frame object count must be between 0 and {MaximumObjectCount}.";
        }
        if(!Enum.IsDefined(
            typeof(RenderPreviewPresentationMode),Request.Frame.PresentationMode)){
            return "Frame presentation mode is unknown.";
        }
        if(Request.Frame.OverlayTriangles is null||Request.Frame.OverlayLines is null){
            return "Frame overlay arrays are required.";
        }
        if(Request.Frame.CameraPreviews is null
            ||Request.Frame.CameraPreviews.Length>MaximumCameraPreviewCount){
            return $"Frame camera preview count must be between 0 and {MaximumCameraPreviewCount}.";
        }
        for(int Index=0;Index<Request.Frame.CameraPreviews.Length;Index++){
            RenderPreviewCameraPass?Preview=Request.Frame.CameraPreviews[Index];
            if(Preview is null){ return $"Frame camera preview {Index} is null."; }
            if(!ValidPositiveDimensions(Preview.Width,Preview.Height)){
                return $"Frame camera preview {Index} dimensions must both be between 1 and {MaximumDimension}.";
            }
            Error=ValidateCamera(Preview.Camera,$"Frame camera preview {Index} camera");
            if(Error is not null){ return Error; }
            if(Preview.ExcludedObjectId is null){
                return $"Frame camera preview {Index} excluded object identity is required.";
            }
            if(Preview.ExcludedObjectId.Length>0
                &&string.IsNullOrWhiteSpace(Preview.ExcludedObjectId)){
                return $"Frame camera preview {Index} excluded object identity is invalid.";
            }
        }
        int OverlayVertexCount;
        try {
            OverlayVertexCount=checked(
                Request.Frame.OverlayTriangles.Length
                +Request.Frame.OverlayLines.Length);
        } catch(OverflowException){
            return $"Frame overlay exceeds {MaximumOverlayVertexCount} vertices.";
        }
        if(OverlayVertexCount>MaximumOverlayVertexCount){
            return $"Frame overlay exceeds {MaximumOverlayVertexCount} vertices.";
        }
        if(Request.Frame.OverlayTriangles.Length%3!=0){
            return "Frame overlay triangle vertices must form complete triangles.";
        }
        if(Request.Frame.OverlayLines.Length%2!=0){
            return "Frame overlay line vertices must form complete lines.";
        }
        for(int Index=0;Index<Request.Frame.OverlayTriangles.Length;Index++){
            string?OverlayError=ValidateOverlayVertex(
                Request.Frame.OverlayTriangles[Index],Index,"triangle");
            if(OverlayError is not null){ return OverlayError; }
        }
        for(int Index=0;Index<Request.Frame.OverlayLines.Length;Index++){
            string?OverlayError=ValidateOverlayVertex(
                Request.Frame.OverlayLines[Index],Index,"line");
            if(OverlayError is not null){ return OverlayError; }
        }
        HashSet<string>Ids=new(StringComparer.Ordinal);
        for(int Index=0;Index<Request.Frame.Objects.Length;Index++){
            RenderPreviewObjectTransform?Object=Request.Frame.Objects[Index];
            if(Object is null){ return $"Frame object {Index} is null."; }
            if(string.IsNullOrWhiteSpace(Object.ObjectId)){ return $"Frame object {Index} identity is required."; }
            if(!Ids.Add(Object.ObjectId)){ return $"Frame object identity is duplicated: {Object.ObjectId}."; }
            Error=ValidateMatrix(Object.World,$"Frame object {Index} world matrix");
            if(Error is not null){ return Error; }
            if(!IsFinite(Object.SelectionWeight)
                ||Object.SelectionWeight<0f||Object.SelectionWeight>1f){
                return $"Frame object {Index} selection weight must be finite and between zero and one.";
            }
        }
        for(int Index=0;Index<Request.Frame.CameraPreviews.Length;Index++){
            string ExcludedObjectId=Request.Frame.CameraPreviews[Index].ExcludedObjectId;
            if(ExcludedObjectId.Length>0&&!Ids.Contains(ExcludedObjectId)){
                return $"Frame camera preview {Index} excluded object is not in the frame roster: {ExcludedObjectId}.";
            }
        }
        return null;
    }

    private static bool ValidPositiveDimensions(int Width,int Height)=>
        Width is>0 and<=MaximumDimension&&Height is>0 and<=MaximumDimension;

    private static string?ValidateDebugName(string?Value,string Name)=>
        Value is null||string.IsNullOrWhiteSpace(Value)||Value.Length>MaximumDebugNameLength
            ?$"Render debug {Name} is invalid."
            :null;

    private static string?ValidateScene(RenderPreviewScene Scene){
        if(Scene.Camera is null){ return "Scene camera is required."; }
        if(Scene.Meshes is null||Scene.Meshes.Length>MaximumMeshCount){
            return $"Scene mesh count must be between 0 and {MaximumMeshCount}.";
        }
        if(Scene.Materials is null||Scene.Materials.Length>MaximumMaterialCount){
            return $"Scene material count must be between 0 and {MaximumMaterialCount}.";
        }
        if(Scene.Objects is null||Scene.Objects.Length>MaximumObjectCount){
            return $"Scene object count must be between 0 and {MaximumObjectCount}.";
        }
        if(Scene.Objects.Length==0){
            if(Scene.Meshes.Length!=0||Scene.Materials.Length!=0){
                return "An empty scene must not retain unused meshes or materials.";
            }
        } else if(Scene.Meshes.Length==0||Scene.Materials.Length==0){
            return "A non-empty scene requires at least one mesh and material.";
        }
        string?Error=ValidateCamera(Scene.Camera,"Camera");
        if(Error is not null){ return Error; }

        for(int MeshIndex=0;MeshIndex<Scene.Meshes.Length;MeshIndex++){
            Error=ValidateMesh(Scene.Meshes[MeshIndex],MeshIndex);
            if(Error is not null){ return Error; }
        }
        for(int MaterialIndex=0;MaterialIndex<Scene.Materials.Length;MaterialIndex++){
            RenderPreviewMaterial?Material=Scene.Materials[MaterialIndex];
            if(Material is null){ return $"Material {MaterialIndex} is null."; }
            if(Material.BaseColor is null||Material.BaseColor.Length!=4){
                return $"Material {MaterialIndex} base color must contain four values.";
            }
            for(int ValueIndex=0;ValueIndex<Material.BaseColor.Length;ValueIndex++){
                if(!IsFinite(Material.BaseColor[ValueIndex])){
                    return $"Material {MaterialIndex} base color must be finite.";
                }
            }
            if(Material.BaseColor[3]<0f||Material.BaseColor[3]>1f){
                return $"Material {MaterialIndex} alpha must be between zero and one.";
            }
        }

        HashSet<string>ObjectIds=new(StringComparer.Ordinal);
        for(int ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            RenderPreviewObject?Object=Scene.Objects[ObjectIndex];
            if(Object is null){ return $"Object {ObjectIndex} is null."; }
            if(string.IsNullOrWhiteSpace(Object.ObjectId)){ return $"Object {ObjectIndex} identity is required."; }
            if(!ObjectIds.Add(Object.ObjectId)){ return $"Object identity is duplicated: {Object.ObjectId}."; }
            if((uint)Object.MeshIndex>=(uint)Scene.Meshes.Length){ return $"Object {ObjectIndex} mesh index is invalid."; }
            if((uint)Object.MaterialIndex>=(uint)Scene.Materials.Length){ return $"Object {ObjectIndex} material index is invalid."; }
            Error=ValidateMatrix(Object.World,$"Object {ObjectIndex} world matrix");
            if(Error is not null){ return Error; }
        }
        return null;
    }

    private static string?ValidateMesh(RenderPreviewMesh?Mesh,int MeshIndex){
        if(Mesh is null){ return $"Mesh {MeshIndex} is null."; }
        if(Mesh.Positions is null||Mesh.Positions.Length<9||Mesh.Positions.Length%3!=0){
            return $"Mesh {MeshIndex} positions must contain at least three xyz vertices.";
        }
        if(Mesh.Normals is null||Mesh.Normals.Length!=Mesh.Positions.Length){
            return $"Mesh {MeshIndex} normals must match its positions.";
        }
        if(Mesh.Indices is null||Mesh.Indices.Length<3||Mesh.Indices.Length%3!=0){
            return $"Mesh {MeshIndex} indices must contain complete triangles.";
        }
        foreach(float Value in Mesh.Positions){
            if(!IsFinite(Value)){ return $"Mesh {MeshIndex} positions must be finite."; }
        }
        foreach(float Value in Mesh.Normals){
            if(!IsFinite(Value)){ return $"Mesh {MeshIndex} normals must be finite."; }
        }
        uint VertexCount=(uint)(Mesh.Positions.Length/3);
        foreach(uint Index in Mesh.Indices){
            if(Index>=VertexCount){ return $"Mesh {MeshIndex} index {Index} is outside its vertex range."; }
        }
        return null;
    }

    private static string?ValidateMatrix(RenderPreviewMatrix4x4?Matrix,string Name){
        if(Matrix?.Values is null||Matrix.Values.Length!=16){ return $"{Name} must contain sixteen values."; }
        foreach(float Value in Matrix.Values){
            if(!IsFinite(Value)){ return $"{Name} must be finite."; }
        }
        return null;
    }

    private static string?ValidateCamera(RenderPreviewCamera?Camera,string Name){
        if(Camera is null){ return $"{Name} is required."; }
        string?Error=ValidateMatrix(Camera.ViewProjection,$"{Name} view-projection");
        if(Error is not null){ return Error; }
        if(Camera.Position is null||Camera.Position.Length!=3){
            return $"{Name} position must contain three values.";
        }
        foreach(float Value in Camera.Position){
            if(!IsFinite(Value)){ return $"{Name} position must be finite."; }
        }
        if(Camera.Rotation is null||Camera.Rotation.Length!=4){
            return $"{Name} rotation must contain four values.";
        }
        foreach(float Value in Camera.Rotation){
            if(!IsFinite(Value)){ return $"{Name} rotation must be finite."; }
        }
        if(!IsFinite(Camera.FovYRadians)||!IsFinite(Camera.NearZ)||!IsFinite(Camera.FarZ)
            ||Camera.FovYRadians<0f||Camera.NearZ<0f||Camera.FarZ<Camera.NearZ){
            return $"{Name} projection must be finite, non-negative and ordered.";
        }
        return null;
    }

    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);

    private static string?ValidateOverlayVertex(
        RenderPreviewOverlayVertex?Vertex,int Index,string Topology){
        if(Vertex is null){ return $"Frame overlay {Topology} vertex {Index} is null."; }
        if(Vertex.Position is null||Vertex.Position.Length!=3
            ||Vertex.Color is null||Vertex.Color.Length!=4){
            return $"Frame overlay {Topology} vertex {Index} has an invalid shape.";
        }
        foreach(float Value in Vertex.Position){
            if(!IsFinite(Value)){
                return $"Frame overlay {Topology} vertex {Index} position must be finite.";
            }
        }
        foreach(float Value in Vertex.Color){
            if(!IsFinite(Value)){
                return $"Frame overlay {Topology} vertex {Index} color must be finite.";
            }
        }
        if(Vertex.Color[3]<0f||Vertex.Color[3]>1f){
            return $"Frame overlay {Topology} vertex {Index} alpha must be between zero and one.";
        }
        return null;
    }
}

public enum RenderPreviewHostCommand:byte {
    Open,
    ReplaceScene,
    UpdateFrame,
    Resize,
    Render,
    Close,
    Snapshot,
}

public enum RenderPreviewHostResultState:byte {
    Succeeded,
    Failed,
    VersionMismatch,
    SessionNotFound,
    StaleSequence,
    HostCrashed,
}

public enum RenderPreviewPresentationMode:byte {
    Lit,
    BaseColor,
    Normal,
    Wireframe,
}

public sealed class RenderPreviewHostRequest {
    public int ProtocolVersion { get;set; }=RenderPreviewHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public Guid SessionId { get;set; }
    public ulong Sequence { get;set; }
    public RenderPreviewHostCommand Command { get;set; }
    public long SurfaceHandle { get;set; }
    public int Width { get;set; }
    public int Height { get;set; }
    public string ShaderArtifactPath { get;set; }=string.Empty;
    public bool CaptureForVerification { get;set; }
    public RenderPreviewScene?Scene { get;set; }
    public RenderPreviewFrame?Frame { get;set; }
    /// <summary>
    /// ADR-0070 B1: on Open, render the built game's own world with its runtime (the game's startup
    /// scenes plus <see cref="RenderPreviewRuntimeView.SceneName"/>) instead of <see cref="Scene"/>.
    /// </summary>
    public RenderPreviewRuntimeView?RuntimeView { get;set; }
    /// <summary>
    /// On Open and ReplaceScene of a runtime view: the edited scene's current <c>*.scene.json</c>
    /// document. Its values are applied to the running world without a build.
    /// </summary>
    public string SceneJson { get;set; }=string.Empty;
}

public sealed class RenderPreviewRuntimeView {
    /// <summary>The built game assembly, usually a copy so that the build output stays writable.</summary>
    public string AssemblyPath { get;set; }=string.Empty;
    /// <summary>The game build's directory, which its content paths are relative to; empty means the assembly's.</summary>
    public string ContentDirectory { get;set; }=string.Empty;
    public string SceneName { get;set; }=string.Empty;
    public bool UseWarp { get;set; }
}

public sealed class RenderPreviewHostResponse {
    public int ProtocolVersion { get;set; }=RenderPreviewHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public Guid SessionId { get;set; }
    public ulong Sequence { get;set; }
    public RenderPreviewHostResultState State { get;set; }
    public uint DeviceGeneration { get;set; }
    public ulong CompletedFence { get;set; }
    public string FrameHash { get;set; }=string.Empty;
    public string DiagnosticCode { get;set; }=string.Empty;
    public string Message { get;set; }=string.Empty;
    public RenderPreviewCameraDiagnostics[]CameraPreviews { get;set; }=[];
    public RenderPreviewDebugSnapshot?DebugSnapshot { get;set; }
    /// <summary>
    /// A runtime view whose scene document changed its structure (nodes or component types) since the
    /// build it runs; the view shows the build's structure with the document's values until the next build.
    /// </summary>
    public bool BuildRequired { get;set; }
}

public enum RenderPreviewDebugGraphKind:byte {
    DeclaredPlan,
}

public enum RenderPreviewDebugDeviceState:byte {
    Ready,
    Suspended,
    Recovering,
    Faulted,
    Disposed,
}

public enum RenderPreviewDebugQueue:byte {
    Graphics,
    Compute,
    Copy,
}

public enum RenderPreviewDebugResourceKind:byte {
    Buffer,
    Texture,
}

public enum RenderPreviewDebugResourceState:byte {
    Undefined,
    External,
    Read,
    Write,
    ReadWrite,
}

public enum RenderPreviewDebugSeverity:byte {
    Corruption,
    Error,
    Warning,
    Information,
    Message,
}

public sealed class RenderPreviewDebugSnapshot {
    public int SnapshotVersion { get;set; }=RenderPreviewHostProtocol.CurrentDebugSnapshotVersion;
    public RenderPreviewDebugGraphKind GraphKind { get;set; }
    public RenderPreviewDebugDeviceState DeviceState { get;set; }
    public uint DeviceGeneration { get;set; }
    public ulong CompletedFence { get;set; }
    public uint FramesRendered { get;set; }
    public int SurfaceWidth { get;set; }
    public int SurfaceHeight { get;set; }
    public int SceneObjectCount { get;set; }
    public int PrimaryObjectDrawCount { get;set; }
    public int ResidentMaterialTextureCount { get;set; }
    public int ErrorOrWorseMessageCount { get;set; }
    public string AdapterDescription { get;set; }=string.Empty;
    public bool IsWarp { get;set; }
    public string QualityProfile { get;set; }=string.Empty;
    public RenderPreviewDebugPass[]Passes { get;set; }=[];
    public RenderPreviewDebugResource[]Resources { get;set; }=[];
    public RenderPreviewDebugTransition[]Transitions { get;set; }=[];
    public RenderPreviewDebugDiagnostic[]Diagnostics { get;set; }=[];
    public bool DiagnosticsTruncated { get;set; }
    public PlatformDebugSnapshot Platform { get;set; }=new();
}

public sealed class RenderPreviewDebugPass {
    public int Index { get;set; }
    public string Name { get;set; }=string.Empty;
    public RenderPreviewDebugQueue Queue { get;set; }
    public int ExecutionIndex { get;set; }
    public int[]Dependencies { get;set; }=[];
}

public sealed class RenderPreviewDebugResource {
    public int Index { get;set; }
    public string Name { get;set; }=string.Empty;
    public RenderPreviewDebugResourceKind Kind { get;set; }
    public ulong ByteSize { get;set; }
    public int Width { get;set; }
    public int Height { get;set; }
    public string Format { get;set; }=string.Empty;
    public bool Imported { get;set; }
    public int FirstUse { get;set; }
    public int LastUse { get;set; }
    public int AliasSlot { get;set; }
}

public sealed class RenderPreviewDebugTransition {
    public int ResourceIndex { get;set; }
    public int PassIndex { get;set; }
    public RenderPreviewDebugResourceState Before { get;set; }
    public RenderPreviewDebugResourceState After { get;set; }
    public RenderPreviewDebugQueue SourceQueue { get;set; }
    public RenderPreviewDebugQueue DestinationQueue { get;set; }
    public bool QueueOwnershipTransfer { get;set; }
}

public sealed class RenderPreviewDebugDiagnostic {
    public RenderPreviewDebugSeverity Severity { get;set; }
    public string Category { get;set; }=string.Empty;
    public string Id { get;set; }=string.Empty;
    public string Description { get;set; }=string.Empty;
}

public enum PlatformDebugSubsystemState:byte {
    NotOwned,
    Unavailable,
    Creating,
    Ready,
    Recovering,
    Faulted,
    Disposed,
}

public enum PlatformDebugAudioBackendMode:byte {
    NotOwned,
    None,
    XAudio2,
    Silent,
}

public enum PlatformDebugInputActionPhase:byte {
    Started,
    Performed,
    Canceled,
}

public enum PlatformDebugInputReleaseReason:byte {
    None,
    PhysicalRelease,
    FocusLost,
    CaptureReleased,
    DeviceRemoved,
    WindowDestroyed,
    ContextChanged,
    Rebound,
}

public sealed class PlatformDebugSnapshot {
    public long CapturedCounter { get;set; }
    public long CounterFrequency { get;set; }
    public string OsDescription { get;set; }=string.Empty;
    public string OsArchitecture { get;set; }=string.Empty;
    public string ProcessArchitecture { get;set; }=string.Empty;
    public string RuntimeDescription { get;set; }=string.Empty;
    public int ProcessId { get;set; }
    public PlatformDebugInputSnapshot Input { get;set; }=new();
    public PlatformDebugAudioSnapshot Audio { get;set; }=new();
}

public sealed class PlatformDebugInputSnapshot {
    public PlatformDebugSubsystemState State { get;set; }
    public int ActiveContextCount { get;set; }
    public int HeldControlCount { get;set; }
    public int ActiveActionCount { get;set; }
    public PlatformDebugInputAction[]Actions { get;set; }=[];
    public bool ActionsTruncated { get;set; }
    public string?Detail { get;set; }
}

public sealed class PlatformDebugInputAction {
    public long TimestampTicks { get;set; }
    public string WindowId { get;set; }=string.Empty;
    public string DeviceId { get;set; }=string.Empty;
    public ulong Sequence { get;set; }
    public string ContextId { get;set; }=string.Empty;
    public string ActionId { get;set; }=string.Empty;
    public PlatformDebugInputActionPhase Phase { get;set; }
    public float RawX { get;set; }
    public float RawY { get;set; }
    public float ValueX { get;set; }
    public float ValueY { get;set; }
    public PlatformDebugInputReleaseReason ReleaseReason { get;set; }
}

public sealed class PlatformDebugAudioSnapshot {
    public PlatformDebugSubsystemState State { get;set; }
    public PlatformDebugAudioBackendMode Mode { get;set; }
    public uint Generation { get;set; }
    public int ActiveVoiceCount { get;set; }
    public int MaximumVoiceCount { get;set; }
    public int BusCount { get;set; }
    public int RestoredVoiceCount { get;set; }
    public int StoppedVoiceCount { get;set; }
    public int RecoveryEventCount { get;set; }
    public int DroppedRecoveryEventCount { get;set; }
    public string?LastFailure { get;set; }
    public string?Detail { get;set; }
}

public sealed class RenderPreviewScene {
    public RenderPreviewCamera?Camera { get;set; }
    public RenderPreviewMesh[]Meshes { get;set; }=[];
    public RenderPreviewMaterial[]Materials { get;set; }=[];
    public RenderPreviewObject[]Objects { get;set; }=[];
}

public sealed class RenderPreviewFrame {
    public RenderPreviewCamera?Camera { get;set; }
    public RenderPreviewPresentationMode PresentationMode { get;set; }
    public RenderPreviewObjectTransform[]Objects { get;set; }=[];
    public RenderPreviewOverlayVertex[]OverlayTriangles { get;set; }=[];
    public RenderPreviewOverlayVertex[]OverlayLines { get;set; }=[];
    public RenderPreviewCameraPass[]CameraPreviews { get;set; }=[];
}

public sealed class RenderPreviewCameraPass {
    public int X { get;set; }
    public int Y { get;set; }
    public int Width { get;set; }
    public int Height { get;set; }
    public RenderPreviewCamera?Camera { get;set; }
    public string ExcludedObjectId { get;set; }=string.Empty;
}

public sealed class RenderPreviewCameraDiagnostics {
    public int SceneObjectCount { get;set; }
    public int FrustumCandidateCount { get;set; }
    public int DrawCallCount { get;set; }
    public uint ViewProjectionHash { get;set; }
}

public sealed class RenderPreviewOverlayVertex {
    public float[]Position { get;set; }=[];
    public float[]Color { get;set; }=[];
}

public sealed class RenderPreviewObjectTransform {
    public string ObjectId { get;set; }=string.Empty;
    public RenderPreviewMatrix4x4 World { get;set; }=RenderPreviewMatrix4x4.Identity;
    public float SelectionWeight { get;set; }
}

public sealed class RenderPreviewCamera {
    public RenderPreviewMatrix4x4 ViewProjection { get;set; }=RenderPreviewMatrix4x4.Identity;
    public float[]Position { get;set; }=[0f,0f,0f];
    /// <summary>The camera's world rotation as an RUF quaternion (R, U, F, W); a runtime view uses it with the projection below.</summary>
    public float[]Rotation { get;set; }=[0f,0f,0f,1f];
    public float FovYRadians { get;set; }
    public float NearZ { get;set; }
    public float FarZ { get;set; }
}

public sealed class RenderPreviewMesh {
    public float[]Positions { get;set; }=[];
    public float[]Normals { get;set; }=[];
    public uint[]Indices { get;set; }=[];
}

public sealed class RenderPreviewMaterial {
    public float[]BaseColor { get;set; }=[1f,1f,1f,1f];
}

public sealed class RenderPreviewObject {
    public string ObjectId { get;set; }=string.Empty;
    public int MeshIndex { get;set; }
    public int MaterialIndex { get;set; }
    public RenderPreviewMatrix4x4 World { get;set; }=RenderPreviewMatrix4x4.Identity;
}

public sealed class RenderPreviewMatrix4x4 {
    public float[]Values { get;set; }=[];

    public static RenderPreviewMatrix4x4 Identity=>new(){
        Values=[
            1f,0f,0f,0f,
            0f,1f,0f,0f,
            0f,0f,1f,0f,
            0f,0f,0f,1f,
        ],
    };
}
