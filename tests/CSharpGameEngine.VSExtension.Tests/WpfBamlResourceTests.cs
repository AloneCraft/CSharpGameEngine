using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class WpfBamlResourceTests {
    [Fact]
    public void BuiltAssemblyEmbedsBamlForEverySourceXaml(){
        string BuildAssembly=VsixBuildIdentity.BuildOutputPath;
        Assert.True(File.Exists(BuildAssembly),$"Build assembly was not found: {BuildAssembly}");

        Assembly Assembly=Assembly.LoadFrom(BuildAssembly);
        string ResourceName=Assembly.GetManifestResourceNames()
            .Single(Name=>Name.EndsWith(".g.resources",StringComparison.OrdinalIgnoreCase));
        using Stream ResourceStream=Assembly.GetManifestResourceStream(ResourceName)!;
        using ResourceReader Reader=new(ResourceStream);
        var Resources=Reader.Cast<DictionaryEntry>()
            .Select(Entry=>Entry.Key.ToString()!.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string XamlDirectory=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
        string[] Expected=Directory.EnumerateFiles(XamlDirectory,"*.xaml",SearchOption.TopDirectoryOnly)
            .Select(FilePath=>Path.GetFileNameWithoutExtension(FilePath).ToLowerInvariant()+".baml")
            .ToArray();

        Assert.NotEmpty(Expected);
        Assert.All(Expected,Resource=>Assert.Contains(Resource,Resources));
    }

    [Fact]
    public void VsixContainsTheSameAssemblyBytesAsBuildOutput(){
        string BuildAssembly=VsixBuildIdentity.BuildOutputPath;
        string VsixPath=Path.Combine(Path.GetDirectoryName(BuildAssembly)!,"CSharpGameEngine.VSExtension.vsix");
        Assert.True(File.Exists(BuildAssembly),$"Build assembly was not found: {BuildAssembly}");
        Assert.True(File.Exists(VsixPath),$"VSIX package was not found: {VsixPath}");

        using ZipArchive Archive=ZipFile.OpenRead(VsixPath);
        ZipArchiveEntry Entry=Archive.Entries.Single(Entry=>
            Entry.FullName.EndsWith("CSharpGameEngine.VSExtension.dll",StringComparison.OrdinalIgnoreCase));
        using Stream PackageStream=Entry.Open();
        using SHA256 Hash=SHA256.Create();
        string PackageHash=ToHex(Hash.ComputeHash(PackageStream));
        string BuildHash=ToHex(HashFile(BuildAssembly));

        Assert.Equal(BuildHash,PackageHash);
    }

    private static byte[] HashFile(string FilePath){
        using SHA256 Hash=SHA256.Create();
        using FileStream Stream=File.OpenRead(FilePath);
        return Hash.ComputeHash(Stream);
    }

    private static string ToHex(byte[] Bytes)=>BitConverter.ToString(Bytes).Replace("-",string.Empty);
}
