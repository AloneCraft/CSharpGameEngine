using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// ADR-0077 stage 6: connects event senders to receivers across the whole compilation. A receiver that wrote no
/// <c>[SystemPhase]</c> runs in the phase of the latest sender, and every receiver is ordered after its senders in the same
/// phase, so a game never states either. A receiver that sits in an earlier phase than a sender would lose the event (events
/// live for one frame), which is an error (SYSGEN041). A receiver with no sender never runs, which is a warning (SYSGEN042).
/// </summary>
internal static class GameSystemEventWiring {
    private static readonly string[]PhaseNames=["FrameInput","FixedSimulation","PhysicsSimulation","PostPhysics","Presentation"];

    internal static ImmutableArray<GameSystemResult>Apply(ImmutableArray<GameSystemResult>Systems,List<Diagnostic>Diagnostics){
        Dictionary<string,List<GameSystemResult>>Senders=new(StringComparer.Ordinal);
        foreach(var System in Systems){
            if(System.Diagnostics.Length>0){ continue; }
            foreach(var Method in System.Methods){
                foreach(var Written in Method.EventWrites){
                    if(!Senders.TryGetValue(Written.EventTypeFullyQualifiedName,out var List)){
                        List=[];
                        Senders.Add(Written.EventTypeFullyQualifiedName,List);
                    }

                    if(!List.Any(Existing=>Existing.GlobalQualifiedName==System.GlobalQualifiedName)){ List.Add(System); }
                }
            }
        }

        var Result=ImmutableArray.CreateBuilder<GameSystemResult>(Systems.Length);
        foreach(var System in Systems){
            var Received=System.Diagnostics.Length>0
                ?[]
                :System.Methods.SelectMany(static Method=>Method.EventReads).Select(static Read=>Read.EventTypeFullyQualifiedName).Distinct(StringComparer.Ordinal).ToList();
            if(Received.Count==0){
                Result.Add(System);
                continue;
            }

            var Phase=System.Phase;
            var After=System.RunsAfterSystemFullyQualifiedNames.ToBuilder();
            List<Diagnostic>Mine=[];
            foreach(var Event in Received){
                var Name=ExtractSimpleTypeName(Event);
                if(!Senders.TryGetValue(Event,out var SenderList)||SenderList.All(Sender=>Sender.GlobalQualifiedName==System.GlobalQualifiedName)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EventNeverSent,System.DeclarationLocation,System.ClassName,Name));
                    continue;
                }

                foreach(var Sender in SenderList){
                    if(Sender.GlobalQualifiedName==System.GlobalQualifiedName){ continue; }
                    if(!System.PhaseIsExplicit&&Sender.Phase>Phase){ Phase=Sender.Phase; }
                }
            }

            foreach(var Event in Received){
                if(!Senders.TryGetValue(Event,out var SenderList)){ continue; }
                foreach(var Sender in SenderList){
                    if(Sender.GlobalQualifiedName==System.GlobalQualifiedName){ continue; }
                    if(Sender.Phase>Phase){
                        Mine.Add(Diagnostic.Create(GameSystemDiagnostics.EventReceivedBeforeItIsSent,System.DeclarationLocation,
                            System.ClassName,ExtractSimpleTypeName(Event),PhaseName(Phase),Sender.ClassName,PhaseName(Sender.Phase)));
                    } else if(Sender.Phase==Phase&&!After.Contains(Sender.GlobalQualifiedName)){
                        After.Add(Sender.GlobalQualifiedName);
                    }
                }
            }

            Diagnostics.AddRange(Mine);
            Result.Add(Mine.Count>0?System.WithDiagnostics([.. Mine]):System.With(Phase,After.ToImmutable()));
        }

        return Result.ToImmutable();
    }

    private static string PhaseName(int Phase)=>Phase>=0&&Phase<PhaseNames.Length?PhaseNames[Phase]:Phase.ToString();

    private static string ExtractSimpleTypeName(string Fqn){
        var Dot=Fqn.LastIndexOf('.');
        return Dot<0?Fqn:Fqn.Substring(Dot+1);
    }
}
