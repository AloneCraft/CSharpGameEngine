using System;
using System.Collections.Generic;
namespace CSharpGameEngine.VSExtension;
internal readonly record struct SceneRuntimeProjectAssembly(string ProjectId,string?AssemblyName);
internal static class SceneRuntimeProcessNameResolver {
    internal static string?Resolve(IEnumerable<SceneRuntimeProjectAssembly>Projects,SceneKey Key){
        foreach(SceneRuntimeProjectAssembly Project in Projects){
            if(string.Equals(Project.ProjectId,Key.ProjectId,StringComparison.Ordinal)){
                return Project.AssemblyName;
            }
        }
        return null;
    }
}