using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
using Xunit;
namespace CSharpGameEngine.VSExtension.Tests;
[CollectionDefinition("AssetThumbnailServiceStatic",DisableParallelization=true)]
public sealed class AssetThumbnailServiceStaticCollection { }
/// <summary>
/// 順番待ちがいっぱいでも、見えているタイルの依頼を取り消さない。タイルは取り消されると頼み直さないので、取り消すと空のまま残る。
/// </summary>
[Collection("AssetThumbnailServiceStatic")]
public sealed class AssetThumbnailAdmissionTests {
    private const BindingFlags StaticFields=BindingFlags.Static|BindingFlags.NonPublic;
    private const BindingFlags InstanceFields=BindingFlags.Instance|BindingFlags.NonPublic;
    [Fact]
    public async Task VisibleRequestsWaitForQueueCapacityWithoutBeingCancelled(){
        var Service=typeof(AssetThumbnailService);
        var Gate=Service.GetField("Gate",StaticFields)!.GetValue(null)!;
        var Running=Service.GetField("Running",StaticFields)!;
        var Queue=Service.GetField("Queue",StaticFields)!.GetValue(null)!;
        var Pending=(IDictionary)Service.GetField("Pending",StaticFields)!.GetValue(null)!;
        var Slots=(SemaphoreSlim?)Service.GetField("QueueSlots",StaticFields)?.GetValue(null);
        var Maximum=(int)Service.GetField("MaximumPending",StaticFields)!.GetRawConstantValue()!;
        var QueueType=Queue.GetType();
        var Count=QueueType.GetProperty("Count")!;
        var First=QueueType.GetProperty("First")!;
        var RemoveFirst=QueueType.GetMethod("RemoveFirst")!;
        var Clear=QueueType.GetMethod("Clear")!;
        var Request=Service.GetMethod("RequestCoreAsync",StaticFields)!;
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeThumbnailAdmissionTests",Guid.NewGuid().ToString("N"));
        var Stops=new List<CancellationTokenSource>();
        var Requests=new List<Task>();
        bool OriginalRunning;
        var OriginalSlots=Slots?.CurrentCount??0;
        lock(Gate){
            OriginalRunning=(bool)Running.GetValue(null)!;
            Assert.False(OriginalRunning);
            Assert.Equal(0,(int)Count.GetValue(Queue)!);
            Assert.Empty(Pending);
            if(Slots is not null){ Assert.Equal(Maximum,OriginalSlots); }
            // 処理役を止めておき、順番待ちの中身をこの試験が 1 件ずつ取り出す。
            Running.SetValue(null,true);
        }
        try {
            Directory.CreateDirectory(DirectoryPath);
            for(var Index=0;Index<Maximum+8;Index++){
                var SourcePath=Path.Combine(DirectoryPath,Index+".png");
                File.WriteAllBytes(SourcePath,new byte[]{0});
                var Stop=new CancellationTokenSource();Stops.Add(Stop);
                var Item=new AssetWorkspaceItem{Kind=AssetWorkspaceKind.Image,Source=new AssetWorkspaceSource{SourcePath=SourcePath}};
                Requests.Add((Task)Request.Invoke(null,new object?[]{Item,Stop.Token,null,AssetThumbnailRequest.DefaultEdge})!);
            }
            lock(Gate){ Assert.InRange((int)Count.GetValue(Queue)!,0,Maximum); }
            var All=Task.WhenAll(Requests);
            var Admitted=0;
            while(Admitted<Maximum+8){
                object? Work=null;
                var Deadline=Stopwatch.StartNew();
                while(Work is null&&!All.IsCompleted){
                    lock(Gate){
                        Assert.InRange((int)Count.GetValue(Queue)!,0,Maximum);
                        var Node=First.GetValue(Queue);
                        if(Node is not null){
                            Work=Node.GetType().GetProperty("Value")!.GetValue(Node)!;
                            RemoveFirst.Invoke(Queue,null);Slots?.Release();
                        }
                    }
                    if(Work is null){
                        Assert.True(Deadline.Elapsed<TimeSpan.FromSeconds(5),"A live request did not resume admission.");
                        await Task.Delay(1);
                    }
                }
                if(Work is null){ break; }
                var Completion=Work.GetType().GetField("Completion",InstanceFields)!.GetValue(Work)!;
                Completion.GetType().GetMethod("TrySetResult")!.Invoke(Completion,new object?[]{null});
                Admitted++;
            }
            Assert.Same(All,await Task.WhenAny(All,Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.All(Requests,Operation=>Assert.Equal(TaskStatus.RanToCompletion,Operation.Status));
            Assert.Equal(Maximum+8,Admitted);
            Assert.All(Stops,Stop=>Assert.False(Stop.IsCancellationRequested));
            lock(Gate){ Assert.Equal(0,(int)Count.GetValue(Queue)!);if(Slots is not null){ Assert.Equal(Maximum,Slots.CurrentCount); } }
        }finally {
            foreach(var Stop in Stops){ Stop.Cancel(); }
            try { await Task.WhenAll(Requests); }catch(OperationCanceledException){ }
            finally {
                lock(Gate){
                    foreach(var Work in Pending.Values.Cast<object>().ToArray()){
                        var Completion=Work.GetType().GetField("Completion",InstanceFields)!.GetValue(Work)!;
                        Completion.GetType().GetMethod("TrySetCanceled",Type.EmptyTypes)!.Invoke(Completion,null);
                        var Stop=(CancellationTokenSource)Work.GetType().GetField("Stop",InstanceFields)!.GetValue(Work)!;
                        try { Stop.Cancel();Stop.Dispose(); }catch(ObjectDisposedException){ }
                    }
                    Clear.Invoke(Queue,null);Pending.Clear();
                    if(Slots is not null){ while(Slots.CurrentCount<OriginalSlots){ Slots.Release(); } }
                    Running.SetValue(null,OriginalRunning);
                }
                foreach(var Stop in Stops){ Stop.Dispose(); }
                if(Directory.Exists(DirectoryPath)){ Directory.Delete(DirectoryPath,true); }
            }
        }
    }
}
