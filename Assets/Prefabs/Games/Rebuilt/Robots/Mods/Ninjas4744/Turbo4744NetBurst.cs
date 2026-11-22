using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Unity.Burst;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Loads the mod's own AOT jobs through Unity's supported Burst mod API.</summary>
    public static class Turbo4744NetBurst
    {
        public static bool Loaded { get; private set; }
        public static string LibraryHash { get; private set; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int StepFunction(IntPtr data);
        public static StepFunction ExecuteStep { get; private set; }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        public static void Load(TextAsset library, string entryPoint)
        {
            if (Application.isEditor || Loaded)
                return;
            if (!library || !BurstCompiler.IsLoadAdditionalLibrarySupported())
            {
                Debug.LogError("[Turbo net] The compiled net library is missing or unsupported.");
                return;
            }
            byte[] bytes = library.bytes;
            using (var hash = SHA256.Create())
                LibraryHash = BitConverter
                    .ToString(hash.ComputeHash(bytes))
                    .Replace("-", "")
                    .ToLowerInvariant();
            string directory = Path.Combine(
                Application.temporaryCachePath,
                "Ninjas4744",
                "Burst",
                LibraryHash
            );
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Ninjas4744NetBurst.dll");
            // A content-addressed filename keeps simultaneously installed revisions separate.
            if (!File.Exists(path) || !Matches(path, LibraryHash))
                File.WriteAllBytes(path, bytes);
            if (!BurstRuntime.LoadAdditionalLibrary(path))
            {
                Debug.LogError("[Turbo net] Unity could not load the compiled net library.");
                return;
            }
            // Resolve our explicit entrypoint directly. BurstCompiler's public
            // delegate wrapper can retain a host-startup fallback in a loaded mod.
            var module = LoadLibraryW(path);
            var pointer =
                module == IntPtr.Zero || string.IsNullOrEmpty(entryPoint)
                    ? IntPtr.Zero
                    : GetProcAddress(module, entryPoint);
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("The compiled net entrypoint was not found");
            var execute = Marshal.GetDelegateForFunctionPointer<StepFunction>(pointer);
            int nativeSize = execute(IntPtr.Zero);
            if (nativeSize != Turbo4744NetSolver.NativeStepSize)
                throw new InvalidOperationException(
                    "Net job ABI mismatch: "
                        + nativeSize
                        + " / "
                        + Turbo4744NetSolver.NativeStepSize
                );
            ExecuteStep = execute;
            Loaded = true;
            Debug.Log("[Turbo net] Native dispatch ready; job bytes " + nativeSize);
        }

        private static bool Matches(string path, string expected)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter
                        .ToString(hash.ComputeHash(stream))
                        .Replace("-", "")
                        .ToLowerInvariant() == expected;
        }
    }
}
