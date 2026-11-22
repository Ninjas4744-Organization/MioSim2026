using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Ninjas4744.MoSim;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// Builds this mod's net entrypoint; no host-game Burst jobs are replaced.
public static class Turbo4744NetBurstBuilder
{
    private const string NativeAsset = Ninjas4744Builder.Root + "/Native/Ninjas4744NetBurst.bytes";

    [Serializable]
    private sealed class Audit
    {
        public string managedSha256,
            editorManagedSha256,
            nativeSha256,
            compilerVersion,
            nativeEntryPoint;
        public string[] options;
        public bool embedded;
    }

    public static string PlayerAssembly =>
        Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/NetBurst/PlayerScripts/Ninjas4744.dll"
        );

    public static void CompileAndEmbed()
    {
        string project = Path.GetFullPath(Application.dataPath + "/..");
        string output = Path.Combine(project, "BuildArtifacts", "NetBurst");
        Directory.CreateDirectory(output);
        string playerScripts = Path.GetDirectoryName(PlayerAssembly);
        Directory.CreateDirectory(playerScripts);
        var compiledScripts = PlayerBuildInterface.CompilePlayerScripts(
            new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None,
            },
            playerScripts
        );
        if (
            compiledScripts.assemblies == null
            || compiledScripts.assemblies.Count == 0
            || !File.Exists(PlayerAssembly)
        )
            throw new InvalidOperationException("Player script compilation failed");
        string assembly = PlayerAssembly;
        string package = PackageInfo
            .FindForAssembly(typeof(Unity.Burst.BurstCompiler).Assembly)
            .resolvedPath;
        string compiler = Path.Combine(package, ".Runtime", "bcl.exe");
        string editor = Path.GetDirectoryName(EditorApplication.applicationPath);
        string libraryPrefix = Path.Combine(output, "Ninjas4744NetBurst");
        var options = new List<string>
        {
            "--platform=Windows",
            "--target=X64_SSE2",
            "--opt-level=2",
            "--root-assembly=" + assembly,
            "--include-root-assembly-references=false",
            "--output=" + libraryPrefix,
            "--temp-folder=" + Path.Combine(output, "Temp"),
            "--key-folder=" + Path.Combine(editor, "Data/PlaybackEngines/WindowsStandaloneSupport"),
            "--decode-folder=" + Path.Combine(project, "Library/Burst"),
            "--global-safety-checks-setting=Off",
            "--is-for-function-pointer",
        };
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetDirectoryName(assembly),
            Path.Combine(editor, "Data/Managed/UnityEngine"),
            Path.Combine(editor, "Data/MonoBleedingEdge/lib/mono/unityaot"),
            Path.Combine(editor, "Data/UnityReferenceAssemblies/unity-4.8-api"),
        };
        foreach (var compiled in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
        foreach (string reference in compiled.compiledAssemblyReferences)
            folders.Add(Path.GetDirectoryName(reference));
        options.Add("--assembly-folder=" + playerScripts);
        // Reference-only netstandard assemblies contain throwing method stubs.
        // Burst must resolve the Mono implementations before any API reference folders.
        string monoRuntime = Path.Combine(editor, "Data/MonoBleedingEdge/lib/mono/unityjit-win32");
        if (!File.Exists(Path.Combine(monoRuntime, "mscorlib.dll")))
            throw new FileNotFoundException("The pinned Mono runtime implementation is missing");
        options.Add("--assembly-folder=" + monoRuntime);
        options.Add("--assembly-folder=" + Path.Combine(monoRuntime, "Facades"));
        options.Add(
            "--assembly-folder="
                + Path.Combine(
                    editor,
                    "Data/PlaybackEngines/WindowsStandaloneSupport/Variations/win64_player_nondevelopment_mono/Data/Managed"
                )
        );
        options.AddRange(
            folders.Where(Directory.Exists).OrderBy(p => p).Select(p => "--assembly-folder=" + p)
        );
        string response = Path.Combine(output, "compiler.rsp");
        File.WriteAllLines(response, options);
        var start = new ProcessStartInfo(compiler, "+burstc @\"" + response + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = project,
        };
        using (var process = Process.Start(start))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180000))
                throw new TimeoutException("Net Burst compilation exceeded three minutes");
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "compiler.log"), log);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Net Burst compilation failed: " + log);
        }
        string native = libraryPrefix + ".dll";
        if (!File.Exists(native))
            throw new FileNotFoundException("Burst did not produce the net library", native);
        var methods = File.ReadAllLines(libraryPrefix + ".txt")
            .Where(line => line.StartsWith("--method="))
            .ToArray();
        if (
            methods.Length != 1
            || !methods[0].Contains("Turbo4744NetSolver")
            || !methods[0].Contains("ExecuteNativeStep")
        )
            throw new InvalidOperationException("Expected only the local net entrypoint");
        string entryPoint = methods[0]
            .Substring(methods[0].LastIndexOf("--", StringComparison.Ordinal) + 2);
        if (entryPoint.Length != 32 || entryPoint.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Unexpected native entrypoint symbol");
        Directory.CreateDirectory(Path.GetDirectoryName(NativeAsset));
        File.Copy(native, NativeAsset, true);
        AssetDatabase.ImportAsset(NativeAsset, ImportAssetOptions.ForceSynchronousImport);
        var prefab = PrefabUtility.LoadPrefabContents(Ninjas4744Builder.RobotPath);
        try
        {
            var net = prefab.GetComponent<Turbo4744>().roofNet.GetComponent<Turbo4744NetSolver>();
            net.nativeLibrary = AssetDatabase.LoadAssetAtPath<TextAsset>(NativeAsset);
            net.nativeEntryPoint = entryPoint;
            PrefabUtility.SaveAsPrefabAsset(prefab, Ninjas4744Builder.RobotPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
        File.WriteAllText(
            Path.Combine(output, "audit.json"),
            JsonUtility.ToJson(
                new Audit
                {
                    managedSha256 = Hash(assembly),
                    editorManagedSha256 = Hash(typeof(Turbo4744NetSolver).Assembly.Location),
                    nativeSha256 = Hash(native),
                    nativeEntryPoint = entryPoint,
                    compilerVersion = PackageInfo
                        .FindForAssembly(typeof(Unity.Burst.BurstCompiler).Assembly)
                        .version,
                    options = options.ToArray(),
                    embedded = true,
                },
                true
            )
        );
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("TURBO_NET_BURST_EMBEDDED " + native);
    }

    public static void VerifyRuntime()
    {
        string path = Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/NetBurst/audit.json"
        );
        var audit = JsonUtility.FromJson<Audit>(File.ReadAllText(path));
        if (
            audit.editorManagedSha256 != Hash(typeof(Turbo4744NetSolver).Assembly.Location)
            || audit.managedSha256 != Hash(PlayerAssembly)
            || audit.nativeSha256 != Hash(NativeAsset)
        )
            throw new InvalidOperationException(
                "Net native library is stale. Run a full Build before Runtime or packaging."
            );
    }

    public static void CopyRuntime(string modRoot)
    {
        VerifyRuntime();
        File.Copy(
            PlayerAssembly,
            Path.Combine(modRoot, "standalonewindows64", "Ninjas4744.dll"),
            true
        );
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter
                .ToString(sha.ComputeHash(stream))
                .Replace("-", "")
                .ToLowerInvariant();
    }
}
