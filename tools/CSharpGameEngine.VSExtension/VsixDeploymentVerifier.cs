using System;
using System.IO;
using System.Security.Cryptography;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Verifies that the assembly loaded by Visual Studio is the same bytes as the latest local build.
/// This is deliberately independent of VS extension caches: a clean build without deployment is
/// still reported as stale rather than being mistaken for a successful VSIX update.
/// </summary>
internal static class VsixDeploymentVerifier {
    internal static VsixDeploymentVerification Verify(string LoadedAssemblyPath,string BuildOutputPath){
        if(string.IsNullOrWhiteSpace(LoadedAssemblyPath)||!File.Exists(LoadedAssemblyPath)){
            return VsixDeploymentVerification.Unavailable("Loaded VSIX assembly was not found.");
        }
        if(string.IsNullOrWhiteSpace(BuildOutputPath)||!File.Exists(BuildOutputPath)){
            return VsixDeploymentVerification.Unavailable("Local VSIX build output was not found.");
        }

        try {
            string LoadedHash=GetSha256(LoadedAssemblyPath);
            string BuildHash=GetSha256(BuildOutputPath);
            return new VsixDeploymentVerification(
                IsVerifiable:true,
                IsCurrent:string.Equals(LoadedHash,BuildHash,StringComparison.OrdinalIgnoreCase),
                LoadedHash,BuildHash,
                string.Equals(LoadedHash,BuildHash,StringComparison.OrdinalIgnoreCase)
                    ?"Loaded VSIX DLL matches the local build output."
                    :"Loaded VSIX DLL differs from the local build output. Run Launch-VSExtension.ps1 to deploy CgeFixed.");
        } catch(Exception Error) when(Error is IOException||Error is UnauthorizedAccessException||Error is CryptographicException){
            return VsixDeploymentVerification.Unavailable($"Unable to hash VSIX assemblies: {Error.Message}");
        }
    }

    private static string GetSha256(string Path){
        using SHA256 Hash=SHA256.Create();
        using FileStream Stream=File.OpenRead(Path);
        byte[] Bytes=Hash.ComputeHash(Stream);
        return BitConverter.ToString(Bytes).Replace("-",string.Empty);
    }
}

internal readonly struct VsixDeploymentVerification {
    internal VsixDeploymentVerification(bool IsVerifiable,bool IsCurrent,string? LoadedHash,string? BuildHash,string Message){
        this.IsVerifiable=IsVerifiable;
        this.IsCurrent=IsCurrent;
        this.LoadedHash=LoadedHash;
        this.BuildHash=BuildHash;
        this.Message=Message;
    }

    internal bool IsVerifiable { get; }
    internal bool IsCurrent { get; }
    internal string? LoadedHash { get; }
    internal string? BuildHash { get; }
    internal string Message { get; }

    internal static VsixDeploymentVerification Unavailable(string Message)=>new(
        IsVerifiable:false,IsCurrent:false,LoadedHash:null,BuildHash:null,Message);

    internal string CompactState=>!this.IsVerifiable?"unverified":this.IsCurrent?"current":"STALE";
}
