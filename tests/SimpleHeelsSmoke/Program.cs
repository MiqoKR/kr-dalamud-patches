using System.Reflection;
using System.Security.Cryptography;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: SimpleHeelsSmoke <official-plugin-directory> <hook-directory>");
}

var source = Path.GetFullPath(args[0]);
var hook = Path.GetFullPath(args[1]);
var sourceDll = Path.Combine(source, "SimpleHeels.dll");
var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceDll)));
var stage = Path.Combine(Path.GetTempPath(), "KR-Dalamud-PatchManager", "simpleheels-smoke", Guid.NewGuid().ToString("N"));

try
{
    CopyDirectory(source, stage);
    var type = Assembly.Load("KR.Dalamud.PatchManager")
        .GetType("KrDalamudPatchManager.Modules.SimpleHeelsPatchCore", throwOnError: true)!;
    var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    type.GetMethod("Patch", flags)!.Invoke(null, new object[] { stage, hook });
    type.GetMethod("Verify", flags)!.Invoke(null, new object[] { stage, hook });

    var afterHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceDll)));
    if (!sourceHash.Equals(afterHash, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The official Simple Heels source DLL was changed during the smoke test.");
    }

    Console.WriteLine("Simple Heels 0.11.1.8 patched DLL verification passed.");
}
finally
{
    if (Directory.Exists(stage))
    {
        Directory.Delete(stage, recursive: true);
    }
}

static void CopyDirectory(string source, string destination)
{
    foreach (var path in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, path);
        var target = Path.Combine(destination, relative);
        if (Directory.Exists(path))
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
        }
    }
}
