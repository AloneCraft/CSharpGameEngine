using System.Collections.ObjectModel;

namespace CSharpGameEngine.Rendering;

public enum RenderResourceKind:byte {
    Buffer,
    Texture,
}

public readonly record struct RenderResourceDescriptor(
    RenderResourceKind Kind,ulong ByteSize,uint Width,uint Height,string? Format){
    public static RenderResourceDescriptor Buffer(ulong ByteSize)=>new(
        RenderResourceKind.Buffer,ByteSize,0,0,null);
    public static RenderResourceDescriptor Texture(uint Width,uint Height,string Format)=>new(
        RenderResourceKind.Texture,0,Width,Height,Format);

    internal void Validate(){
        switch(this.Kind){
            case RenderResourceKind.Buffer when this.ByteSize==0:
                throw new ArgumentOutOfRangeException(nameof(this.ByteSize),"A render buffer cannot be empty.");
            case RenderResourceKind.Texture when this.Width==0||this.Height==0||string.IsNullOrWhiteSpace(this.Format):
                throw new ArgumentException("A render texture requires non-zero dimensions and a format.");
            case not (RenderResourceKind.Buffer or RenderResourceKind.Texture):
                throw new ArgumentOutOfRangeException(nameof(this.Kind));
        }
    }
}

public readonly struct RenderResourceHandle<T>:IEquatable<RenderResourceHandle<T>> {
    internal RenderResourceHandle(int Index,uint Scope){
        this.Index=Index;
        this.Scope=Scope;
    }
    public int Index { get; }
    internal uint Scope { get; }
    public bool Equals(RenderResourceHandle<T> Other)=>this.Index==Other.Index&&this.Scope==Other.Scope;
    public override bool Equals(object?Object)=>Object is RenderResourceHandle<T> Other&&this.Equals(Other);
    public override int GetHashCode()=>HashCode.Combine(this.Index,this.Scope);
    public static bool operator==(RenderResourceHandle<T> Left,RenderResourceHandle<T> Right)=>Left.Equals(Right);
    public static bool operator!=(RenderResourceHandle<T> Left,RenderResourceHandle<T> Right)=>!Left.Equals(Right);
}

public readonly struct RenderPassHandle:IEquatable<RenderPassHandle> {
    internal RenderPassHandle(int Index,uint Scope){
        this.Index=Index;
        this.Scope=Scope;
    }
    public int Index { get; }
    internal uint Scope { get; }
    public bool Equals(RenderPassHandle Other)=>this.Index==Other.Index&&this.Scope==Other.Scope;
    public override bool Equals(object?Object)=>Object is RenderPassHandle Other&&this.Equals(Other);
    public override int GetHashCode()=>HashCode.Combine(this.Index,this.Scope);
    public static bool operator==(RenderPassHandle Left,RenderPassHandle Right)=>Left.Equals(Right);
    public static bool operator!=(RenderPassHandle Left,RenderPassHandle Right)=>!Left.Equals(Right);
}

public enum RenderResourceAccess:byte {
    Read,
    Write,
    ReadWrite,
}

public enum RenderQueue:byte {
    Graphics,
    Compute,
    Copy,
}

public enum RenderResourceState:byte {
    Undefined,
    External,
    Read,
    Write,
    ReadWrite,
}

public sealed class RenderGraphValidationException(string Message):InvalidOperationException(Message);

public readonly record struct RenderTransitionPlan(
    int ResourceIndex,int PassIndex,
    RenderResourceState Before,RenderResourceState After,
    RenderQueue SourceQueue,RenderQueue DestinationQueue,
    bool QueueOwnershipTransfer);

public sealed class RenderResourcePlan {
    internal RenderResourcePlan(
        int Index,string Name,RenderResourceDescriptor Descriptor,bool Imported,
        int FirstUse,int LastUse,int AliasSlot){
        this.Index=Index;
        this.Name=Name;
        this.Descriptor=Descriptor;
        this.Imported=Imported;
        this.FirstUse=FirstUse;
        this.LastUse=LastUse;
        this.AliasSlot=AliasSlot;
    }
    public int Index { get; }
    public string Name { get; }
    public RenderResourceDescriptor Descriptor { get; }
    public bool Imported { get; }
    public int FirstUse { get; }
    public int LastUse { get; }
    public int AliasSlot { get; }
}

public sealed class RenderPassPlan {
    private readonly ReadOnlyCollection<int>DependenciesStorage;
    internal RenderPassPlan(
        int Index,string Name,RenderQueue Queue,int ExecutionIndex,int[]Dependencies){
        this.Index=Index;
        this.Name=Name;
        this.Queue=Queue;
        this.ExecutionIndex=ExecutionIndex;
        this.DependenciesStorage=Array.AsReadOnly(Dependencies);
    }
    public int Index { get; }
    public string Name { get; }
    public RenderQueue Queue { get; }
    public int ExecutionIndex { get; }
    public IReadOnlyList<int>Dependencies=>this.DependenciesStorage;
}

public sealed class RenderGraph {
    private readonly ReadOnlyCollection<RenderResourcePlan>ResourcesStorage;
    private readonly ReadOnlyCollection<RenderPassPlan>PassesStorage;
    private readonly ReadOnlyCollection<RenderTransitionPlan>TransitionsStorage;
    internal RenderGraph(
        RenderResourcePlan[]Resources,RenderPassPlan[]Passes,
        RenderTransitionPlan[]Transitions){
        this.ResourcesStorage=Array.AsReadOnly(Resources);
        this.PassesStorage=Array.AsReadOnly(Passes);
        this.TransitionsStorage=Array.AsReadOnly(Transitions);
    }
    public IReadOnlyList<RenderResourcePlan>Resources=>this.ResourcesStorage;
    public IReadOnlyList<RenderPassPlan>Passes=>this.PassesStorage;
    public IReadOnlyList<RenderTransitionPlan>Transitions=>this.TransitionsStorage;
}

public sealed class RenderPassBuilder {
    private readonly RenderGraphBuilder Owner;
    private readonly int PassIndex;
    private bool Active=true;
    internal RenderPassBuilder(RenderGraphBuilder Owner,int PassIndex){
        this.Owner=Owner;
        this.PassIndex=PassIndex;
    }
    public void Read<T>(RenderResourceHandle<T>Resource)=>
        this.Use(Resource,RenderResourceAccess.Read);
    public void Write<T>(RenderResourceHandle<T>Resource)=>
        this.Use(Resource,RenderResourceAccess.Write);
    public void ReadWrite<T>(RenderResourceHandle<T>Resource)=>
        this.Use(Resource,RenderResourceAccess.ReadWrite);
    public void DependsOn(RenderPassHandle Pass){
        this.EnsureActive();
        this.Owner.AddDependencyToPass(this.PassIndex,Pass);
    }
    internal void Complete()=>this.Active=false;
    private void Use<T>(RenderResourceHandle<T>Resource,RenderResourceAccess Access){
        this.EnsureActive();
        this.Owner.AddUse(this.PassIndex,Resource,Access);
    }
    private void EnsureActive(){
        if(!this.Active){ throw new InvalidOperationException("A pass declaration cannot be changed after AddPass returns."); }
    }
}

public sealed class RenderGraphBuilder {
    private sealed record ResourceDefinition(
        string Name,RenderResourceDescriptor Descriptor,bool Imported,Type ValueType);
    private sealed class PassDefinition(string Name,RenderQueue Queue) {
        internal string Name { get; }=Name;
        internal RenderQueue Queue { get; }=Queue;
        internal List<ResourceUse>Uses { get; }=[];
        internal HashSet<int>ExplicitDependencies { get; }=[];
    }
    private readonly record struct ResourceUse(int Resource,RenderResourceAccess Access);
    private sealed class AliasSlot(RenderResourceDescriptor Descriptor,int LastUse) {
        internal RenderResourceDescriptor Descriptor { get; }=Descriptor;
        internal int LastUse=LastUse;
    }

    private static int NextScope;
    private readonly uint Scope=unchecked((uint)Interlocked.Increment(ref NextScope));
    private readonly List<ResourceDefinition>Resources=[];
    private readonly List<PassDefinition>Passes=[];
    private bool Frozen;

    public RenderResourceHandle<T>CreateTransient<T>(
        string Name,RenderResourceDescriptor Descriptor)=>
        this.CreateResource<T>(Name,Descriptor,Imported:false);

    public RenderResourceHandle<T>Import<T>(
        string Name,RenderResourceDescriptor Descriptor)=>
        this.CreateResource<T>(Name,Descriptor,Imported:true);

    public RenderPassHandle AddPass(string Name,Action<RenderPassBuilder>Setup)=>
        this.AddPass(Name,RenderQueue.Graphics,Setup);

    public RenderPassHandle AddPass(
        string Name,RenderQueue Queue,Action<RenderPassBuilder>Setup){
        this.ThrowIfFrozen();
        if(string.IsNullOrWhiteSpace(Name)){ throw new ArgumentException("A render pass requires a name.",nameof(Name)); }
        if(Queue is not (RenderQueue.Graphics or RenderQueue.Compute or RenderQueue.Copy)){
            throw new ArgumentOutOfRangeException(nameof(Queue));
        }
        ArgumentNullException.ThrowIfNull(Setup);
        int Index=this.Passes.Count;
        this.Passes.Add(new PassDefinition(Name,Queue));
        RenderPassBuilder Builder=new(this,Index);
        try {
            Setup(Builder);
        }
        catch {
            this.Passes.RemoveAt(Index);
            throw;
        }
        finally {
            Builder.Complete();
        }
        return new RenderPassHandle(Index,this.Scope);
    }

    public void AddDependency(RenderPassHandle Before,RenderPassHandle After){
        this.ThrowIfFrozen();
        int BeforeIndex=this.ValidatePass(Before);
        int AfterIndex=this.ValidatePass(After);
        if(BeforeIndex==AfterIndex){ throw new RenderGraphValidationException("A render pass cannot depend on itself."); }
        this.Passes[AfterIndex].ExplicitDependencies.Add(BeforeIndex);
    }

    public RenderGraph Compile(){
        this.ThrowIfFrozen();
        this.Frozen=true;
        if(this.Passes.Count==0){ throw new RenderGraphValidationException("A render graph requires at least one pass."); }
        HashSet<int>[]Dependencies=this.BuildDependencies();
        int[]ExecutionOrder=TopologicalSort(Dependencies);
        int[]ExecutionIndex=new int[this.Passes.Count];
        for(int Index=0;Index<ExecutionOrder.Length;Index++){ ExecutionIndex[ExecutionOrder[Index]]=Index; }
        RenderResourcePlan[]Resources=this.BuildResourcePlans(ExecutionIndex);
        RenderPassPlan[]Passes=new RenderPassPlan[this.Passes.Count];
        for(int Index=0;Index<this.Passes.Count;Index++){
            int[]OrderedDependencies=[.. Dependencies[Index].Order()];
            PassDefinition Definition=this.Passes[Index];
            Passes[Index]=new RenderPassPlan(
                Index,Definition.Name,Definition.Queue,ExecutionIndex[Index],OrderedDependencies);
        }
        Array.Sort(Passes,static(Left,Right)=>Left.ExecutionIndex.CompareTo(Right.ExecutionIndex));
        RenderTransitionPlan[]Transitions=this.BuildTransitions(ExecutionOrder);
        return new RenderGraph(Resources,Passes,Transitions);
    }

    internal void AddDependencyToPass(int PassIndex,RenderPassHandle Before){
        this.ThrowIfFrozen();
        int BeforeIndex=this.ValidatePass(Before);
        if(BeforeIndex==PassIndex){ throw new RenderGraphValidationException("A render pass cannot depend on itself."); }
        this.Passes[PassIndex].ExplicitDependencies.Add(BeforeIndex);
    }

    internal void AddUse<T>(
        int PassIndex,RenderResourceHandle<T>Resource,RenderResourceAccess Access){
        this.ThrowIfFrozen();
        int ResourceIndex=this.ValidateResource(Resource);
        PassDefinition Pass=this.Passes[PassIndex];
        if(Pass.Uses.Any(Use=>Use.Resource==ResourceIndex)){
            throw new RenderGraphValidationException(
                $"Pass '{Pass.Name}' declares resource '{this.Resources[ResourceIndex].Name}' more than once; use ReadWrite for combined access.");
        }
        Pass.Uses.Add(new ResourceUse(ResourceIndex,Access));
    }

    private RenderResourceHandle<T>CreateResource<T>(
        string Name,RenderResourceDescriptor Descriptor,bool Imported){
        this.ThrowIfFrozen();
        if(string.IsNullOrWhiteSpace(Name)){ throw new ArgumentException("A render resource requires a name.",nameof(Name)); }
        Descriptor.Validate();
        int Index=this.Resources.Count;
        this.Resources.Add(new ResourceDefinition(Name,Descriptor,Imported,typeof(T)));
        return new RenderResourceHandle<T>(Index,this.Scope);
    }

    private HashSet<int>[]BuildDependencies(){
        HashSet<int>[]Dependencies=new HashSet<int>[this.Passes.Count];
        for(int Index=0;Index<Dependencies.Length;Index++){
            Dependencies[Index]=[.. this.Passes[Index].ExplicitDependencies];
        }
        int?[]LastWriters=new int?[this.Resources.Count];
        HashSet<int>[]Readers=new HashSet<int>[this.Resources.Count];
        bool[]Initialized=new bool[this.Resources.Count];
        for(int Resource=0;Resource<this.Resources.Count;Resource++){
            Readers[Resource]=[];
            Initialized[Resource]=this.Resources[Resource].Imported;
        }
        for(int PassIndex=0;PassIndex<this.Passes.Count;PassIndex++){
            foreach(ResourceUse Use in this.Passes[PassIndex].Uses){
                int Resource=Use.Resource;
                switch(Use.Access){
                    case RenderResourceAccess.Read:
                        if(!Initialized[Resource]){ throw this.ReadBeforeWrite(PassIndex,Resource); }
                        if(LastWriters[Resource] is int Writer){ Dependencies[PassIndex].Add(Writer); }
                        Readers[Resource].Add(PassIndex);
                        break;
                    case RenderResourceAccess.Write:
                        AddWriteDependencies(Dependencies[PassIndex],LastWriters[Resource],Readers[Resource]);
                        Initialized[Resource]=true;
                        LastWriters[Resource]=PassIndex;
                        Readers[Resource].Clear();
                        break;
                    case RenderResourceAccess.ReadWrite:
                        if(!Initialized[Resource]){ throw this.ReadBeforeWrite(PassIndex,Resource); }
                        AddWriteDependencies(Dependencies[PassIndex],LastWriters[Resource],Readers[Resource]);
                        LastWriters[Resource]=PassIndex;
                        Readers[Resource].Clear();
                        break;
                    default:throw new ArgumentOutOfRangeException();
                }
            }
        }
        return Dependencies;
    }

    private RenderResourcePlan[]BuildResourcePlans(int[]ExecutionIndex){
        int[]FirstUse=Enumerable.Repeat(int.MaxValue,this.Resources.Count).ToArray();
        int[]LastUse=Enumerable.Repeat(-1,this.Resources.Count).ToArray();
        for(int Pass=0;Pass<this.Passes.Count;Pass++){
            int Order=ExecutionIndex[Pass];
            foreach(ResourceUse Use in this.Passes[Pass].Uses){
                FirstUse[Use.Resource]=Math.Min(FirstUse[Use.Resource],Order);
                LastUse[Use.Resource]=Math.Max(LastUse[Use.Resource],Order);
            }
        }
        for(int Resource=0;Resource<this.Resources.Count;Resource++){
            if(LastUse[Resource]<0){
                throw new RenderGraphValidationException($"Render resource '{this.Resources[Resource].Name}' is never used.");
            }
        }
        int[]Aliases=Enumerable.Repeat(-1,this.Resources.Count).ToArray();
        List<AliasSlot>Slots=[];
        foreach(int Resource in Enumerable.Range(0,this.Resources.Count)
            .Where(Index=>!this.Resources[Index].Imported)
            .OrderBy(Index=>FirstUse[Index]).ThenBy(static Index=>Index)){
            ResourceDefinition Definition=this.Resources[Resource];
            int Slot=Slots.FindIndex(Candidate=>
                Candidate.Descriptor==Definition.Descriptor&&Candidate.LastUse<FirstUse[Resource]);
            if(Slot<0){
                Slot=Slots.Count;
                Slots.Add(new AliasSlot(Definition.Descriptor,LastUse[Resource]));
            }
            else {
                Slots[Slot].LastUse=LastUse[Resource];
            }
            Aliases[Resource]=Slot;
        }
        RenderResourcePlan[]Plans=new RenderResourcePlan[this.Resources.Count];
        for(int Index=0;Index<Plans.Length;Index++){
            ResourceDefinition Definition=this.Resources[Index];
            Plans[Index]=new RenderResourcePlan(
                Index,Definition.Name,Definition.Descriptor,Definition.Imported,
                FirstUse[Index],LastUse[Index],Aliases[Index]);
        }
        return Plans;
    }

    private RenderTransitionPlan[]BuildTransitions(int[]ExecutionOrder){
        RenderResourceState[]States=new RenderResourceState[this.Resources.Count];
        RenderQueue[]Queues=new RenderQueue[this.Resources.Count];
        bool[]HasQueue=new bool[this.Resources.Count];
        for(int Resource=0;Resource<this.Resources.Count;Resource++){
            States[Resource]=this.Resources[Resource].Imported
                ?RenderResourceState.External
                :RenderResourceState.Undefined;
        }
        List<RenderTransitionPlan>Transitions=[];
        foreach(int PassIndex in ExecutionOrder){
            PassDefinition Pass=this.Passes[PassIndex];
            foreach(ResourceUse Use in Pass.Uses.OrderBy(static Use=>Use.Resource)){
                RenderResourceState After=Use.Access switch {
                    RenderResourceAccess.Read=>RenderResourceState.Read,
                    RenderResourceAccess.Write=>RenderResourceState.Write,
                    RenderResourceAccess.ReadWrite=>RenderResourceState.ReadWrite,
                    _=>throw new ArgumentOutOfRangeException(),
                };
                int Resource=Use.Resource;
                RenderQueue SourceQueue=HasQueue[Resource]?Queues[Resource]:Pass.Queue;
                bool OwnershipTransfer=HasQueue[Resource]&&SourceQueue!=Pass.Queue;
                if(States[Resource]!=After||OwnershipTransfer){
                    Transitions.Add(new RenderTransitionPlan(
                        Resource,PassIndex,States[Resource],After,
                        SourceQueue,Pass.Queue,OwnershipTransfer));
                }
                States[Resource]=After;
                Queues[Resource]=Pass.Queue;
                HasQueue[Resource]=true;
            }
        }
        return [.. Transitions];
    }

    private int ValidatePass(RenderPassHandle Pass){
        if(Pass.Scope!=this.Scope||(uint)Pass.Index>=(uint)this.Passes.Count){
            throw new ArgumentException("The render pass belongs to another graph builder.",nameof(Pass));
        }
        return Pass.Index;
    }

    private int ValidateResource<T>(RenderResourceHandle<T>Resource){
        if(Resource.Scope!=this.Scope||(uint)Resource.Index>=(uint)this.Resources.Count
            ||this.Resources[Resource.Index].ValueType!=typeof(T)){
            throw new ArgumentException("The render resource belongs to another graph builder.",nameof(Resource));
        }
        return Resource.Index;
    }

    private RenderGraphValidationException ReadBeforeWrite(int Pass,int Resource)=>new(
        $"Pass '{this.Passes[Pass].Name}' reads transient resource '{this.Resources[Resource].Name}' before its first write.");

    private static void AddWriteDependencies(
        HashSet<int>Dependencies,int?Writer,HashSet<int>Readers){
        if(Writer is int PreviousWriter){ Dependencies.Add(PreviousWriter); }
        Dependencies.UnionWith(Readers);
    }

    private static int[]TopologicalSort(HashSet<int>[]Dependencies){
        List<int>[]Dependents=new List<int>[Dependencies.Length];
        int[]InDegree=new int[Dependencies.Length];
        for(int Index=0;Index<Dependencies.Length;Index++){ Dependents[Index]=[]; }
        for(int Pass=0;Pass<Dependencies.Length;Pass++){
            InDegree[Pass]=Dependencies[Pass].Count;
            foreach(int Dependency in Dependencies[Pass]){ Dependents[Dependency].Add(Pass); }
        }
        PriorityQueue<int,int>Ready=new();
        for(int Pass=0;Pass<InDegree.Length;Pass++){
            if(InDegree[Pass]==0){ Ready.Enqueue(Pass,Pass); }
        }
        int[]Result=new int[Dependencies.Length];
        int Count=0;
        while(Ready.TryDequeue(out int Pass,out _)){
            Result[Count++]=Pass;
            foreach(int Dependent in Dependents[Pass]){
                if(--InDegree[Dependent]==0){ Ready.Enqueue(Dependent,Dependent); }
            }
        }
        if(Count!=Result.Length){ throw new RenderGraphValidationException("The render graph contains a dependency cycle."); }
        return Result;
    }

    private void ThrowIfFrozen(){
        if(this.Frozen){ throw new InvalidOperationException("The render graph builder is already compiled."); }
    }
}
