// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.CrashReporting;
using UnityEditor.Il2Cpp;
using UnityEditor.Scripting;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEditor
{
    [MovedFrom("UnityEditor.LinuxStandalone")]
    public abstract class Sysroot
    {
        public abstract string Name { get; }
        public abstract bool Initialize();
        public abstract string HostPlatform { get; }
        public abstract string HostArch { get; }
        public abstract string TargetPlatform { get; }
        public abstract string TargetArch { get; }
        public abstract IEnumerable<string> GetIl2CppArguments();
    }
}

namespace UnityEditorInternal
{
    internal static class SysrootManager
    {
        private static Dictionary<string, Sysroot> _knownSysroots = null;
        private static Dictionary<string, string> _archMap = null;
        private static string _hostPlatform = null;
        private static string _hostArch = null;

        private static string MakeKey(string hostPlatform, string hostArch, string targetPlatform, string targetArch)
        {
            return $"{hostPlatform.ToLower()},{hostArch.ToLower()},{targetPlatform.ToLower()},{targetArch.ToLower()}";
        }

        private static string MakeKey(string targetPlatform, string targetArch)
        {
            return MakeKey(_hostPlatform, _hostArch, targetPlatform, targetArch);
        }

        private static string MakeKey(Sysroot sysroot)
        {
            return MakeKey(sysroot.HostPlatform, sysroot.HostArch, sysroot.TargetPlatform, sysroot.TargetArch);
        }

        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            CreateArchMapping();
            RegisterSysroots();
        }

        private static void CreateArchMapping()
        {
            _archMap = new Dictionary<string, string>();
            _archMap.Add("amd64", "x86_64");
            _archMap.Add("i686", "x86");
        }

        private static string MapArch(string arch)
        {
            string mapped;
            if (_archMap.TryGetValue(arch.ToLower(), out mapped))
                return mapped;
            return arch.ToLower();
        }

        private static void RegisterSysroots()
        {
            _knownSysroots = new Dictionary<string, Sysroot>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<Sysroot>())
            {
                var sysroot = Activator.CreateInstance(type, new object[] {}, new object[] {}) as Sysroot;
                if (sysroot != null)
                {
                    if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNITY_SYSROOT_DEBUG")))
                        UnityEngine.Debug.Log($"Found sysroot: {sysroot.Name}, hp={sysroot.HostPlatform}, ha={sysroot.HostArch}, tp={sysroot.TargetPlatform}, ta={sysroot.TargetArch}");
                    _knownSysroots.Add(MakeKey(sysroot.HostPlatform, sysroot.HostArch, sysroot.TargetPlatform, sysroot.TargetArch), sysroot);
                }
            }
        }

        private static bool GetTargetPlatformAndArchFromBuildTarget(BuildTarget target, out string targetPlatform, out string targetArch)
        {
            switch (target)
            {
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.CloudRendering:
                    targetPlatform = "linux";
                    targetArch = "x86_64";
                    return true;
                case BuildTarget.WebGL:
                    targetPlatform = "webgl";
                    targetArch = "";
                    return true;
            }

            targetPlatform = null;
            targetArch = null;
            return false;
        }

        private static void GetPosixPlatformAndArch()
        {
            var p = new Process();
            p.StartInfo.FileName = "uname";
            p.StartInfo.Arguments = "-s -m";
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.Start();
            var parts = p.StandardOutput.ReadToEnd().Split(new char[] { ' ', '\r', '\n' });
            p.WaitForExit();
            if (parts.Length > 1)
            {
                _hostPlatform = parts[0].ToLower();
                _hostArch = MapArch(parts[1]);
            }
        }

        private static string AllowEnvironmentOverride(string origValue, string envVar)
        {
            string envValue = Environment.GetEnvironmentVariable(envVar);
            return envValue == null ? origValue : envValue;
        }

        private static bool GetHostPlatformAndArch()
        {
            if (_hostPlatform != null && _hostArch != null)
                return true;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    _hostPlatform = "windows";
                    _hostArch = MapArch(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"));
                    break;
                case PlatformID.Unix:
                    GetPosixPlatformAndArch();
                    break;
            }

            _hostPlatform = AllowEnvironmentOverride(_hostPlatform, "UNITY_SYSROOT_HOST_PLATFORM");
            _hostArch = AllowEnvironmentOverride(_hostArch, "UNITY_SYSROOT_HOST_ARCH");

            return _hostPlatform != null && _hostArch != null;
        }

        public static Sysroot FindSysroot(BuildTarget target)
        {
            string targetPlatform, targetArch;
            if (!GetTargetPlatformAndArchFromBuildTarget(target, out targetPlatform, out targetArch))
                return null;

            return FindSysroot(targetPlatform, targetArch);
        }

        private static Sysroot FindSysroot(string targetPlatform, string targetArch)
        {
            if (!GetHostPlatformAndArch())
                return null;

            Sysroot sysroot;
            if (!_knownSysroots.TryGetValue(MakeKey(targetPlatform, targetArch), out sysroot))
                return null;

            if (!sysroot.Initialize())
            {
                UnityEngine.Debug.Log($"Failed to initialize sysroot {sysroot.Name}");
                return null;
            }

            return sysroot;
        }

        public static string HostTargetTuple(BuildTarget buildTarget)
        {
            if (GetHostPlatformAndArch())
            {
                string targetPlatform;
                string targetArch;
                if (GetTargetPlatformAndArchFromBuildTarget(buildTarget, out targetPlatform, out targetArch))
                {
                    string host;
                    switch (_hostPlatform)
                    {
                        case "darwin":
                            host = $"macos-{_hostArch}";
                            break;
                        case "windows":
                            host = $"win-{_hostArch}";
                            break;
                        default:
                            host = $"{_hostPlatform}-{_hostArch}";
                            break;
                    }
                    string target = $"{targetPlatform}-{targetArch}";
                    return host == target ? target : $"{host}-{target}";
                }
            }
            return null;
        }

        public static IEnumerable<Sysroot> EnumerateSysroots()
        {
            foreach (Sysroot sysroot in _knownSysroots.Values)
            {
                yield return sysroot;
            }
        }
    }

    internal class IL2CPPUtils
    {
        private static readonly string[] BaseDefinesWindows = new[]
        {
            "_CRT_SECURE_NO_WARNINGS",
            "_WINSOCK_DEPRECATED_NO_WARNINGS",
            "WIN32",
            "WINDOWS",
            "_UNICODE",
            "UNICODE",
        };

        private static readonly string[] BaseDefines20 = new[]
        {
            "ALL_INTERIOR_POINTERS=1",
            "GC_GCJ_SUPPORT=1",
            "JAVA_FINALIZATION=1",
            "NO_EXECUTE_PERMISSION=1",
            "GC_NO_THREADS_DISCOVERY=1",
            "IGNORE_DYNAMIC_LOADING=1",
            "GC_DONT_REGISTER_MAIN_STATIC_DATA=1",
            "GC_VERSION_MAJOR=7",
            "GC_VERSION_MINOR=7",
            "GC_VERSION_MICRO=0",
            "GC_THREADS=1",
            "USE_MMAP=1",
            "USE_MUNMAP=1",
        }.ToArray();

        private static readonly string[] BaseDefines46 = BaseDefines20.Concat(new[]
        {
            "NET_4_0=1",
            "UNITY_AOT=1",
            "NET_STANDARD_2_0=1",
            "NET_UNITY_4_8=1",
            "NET_STANDARD=1",
        }).ToArray();

        public const string BinaryMetadataSuffix = "-metadata.dat";

        internal static IIl2CppPlatformProvider PlatformProviderForNotModularPlatform(BuildTarget target, bool developmentBuild)
        {
            throw new Exception("Platform unsupported, or already modular.");
        }

        internal static IL2CPPBuilder RunIl2Cpp(string tempFolder, string stagingAreaData, IIl2CppPlatformProvider platformProvider, Action<string> modifyOutputBeforeCompile, RuntimeClassRegistry runtimeClassRegistry)
        {
            var builder = new IL2CPPBuilder(tempFolder, stagingAreaData, platformProvider, modifyOutputBeforeCompile, runtimeClassRegistry, IL2CPPUtils.UseIl2CppCodegenWithMonoBackend(platformProvider.namedBuildTarget));
            builder.Run();
            return builder;
        }

        internal static IL2CPPBuilder RunIl2Cpp(string stagingAreaData, IIl2CppPlatformProvider platformProvider, Action<string> modifyOutputBeforeCompile, RuntimeClassRegistry runtimeClassRegistry)
        {
            var builder = new IL2CPPBuilder(stagingAreaData, stagingAreaData, platformProvider, modifyOutputBeforeCompile, runtimeClassRegistry, IL2CPPUtils.UseIl2CppCodegenWithMonoBackend(platformProvider.namedBuildTarget));
            builder.Run();
            return builder;
        }

        internal static IL2CPPBuilder RunCompileAndLink(string tempFolder, string stagingAreaData, IIl2CppPlatformProvider platformProvider, Action<string> modifyOutputBeforeCompile, RuntimeClassRegistry runtimeClassRegistry, string il2cppBuildCacheSource)
        {
            var builder = new IL2CPPBuilder(tempFolder, stagingAreaData, platformProvider, modifyOutputBeforeCompile, runtimeClassRegistry, IL2CPPUtils.UseIl2CppCodegenWithMonoBackend(platformProvider.namedBuildTarget));
            builder.RunCompileAndLink(il2cppBuildCacheSource);
            return builder;
        }

        internal static void CopyEmbeddedResourceFiles(string tempFolder, string destinationFolder)
        {
            foreach (var file in Directory.GetFiles(Paths.Combine(IL2CPPBuilder.GetCppOutputDirectory(tempFolder), "Data", "Resources")).Where(f => f.EndsWith("-resources.dat")))
                File.Copy(file, Paths.Combine(destinationFolder, Path.GetFileName(file)), true);
        }

        internal static void CopySymmapFile(string tempFolder, string destinationFolder)
        {
            CopySymmapFile(tempFolder, destinationFolder, string.Empty);
        }

        internal static void CopySymmapFile(string tempFolder, string destinationFolder, string destinationFileNameSuffix)
        {
            const string fileName = "SymbolMap";
            var file = Path.Combine(tempFolder, fileName);
            if (File.Exists(file))
                File.Copy(file, Path.Combine(destinationFolder, fileName + destinationFileNameSuffix), true);
        }

        internal static void CopyMetadataFiles(string tempFolder, string destinationFolder)
        {
            foreach (var file in Directory.GetFiles(Paths.Combine(IL2CPPBuilder.GetCppOutputDirectory(tempFolder), "Data", "Metadata")).Where(f => f.EndsWith(BinaryMetadataSuffix)))
                File.Copy(file, Paths.Combine(destinationFolder, Path.GetFileName(file)), true);
        }

        internal static string ApiCompatibilityLevelToDotNetProfileArgument(ApiCompatibilityLevel compatibilityLevel, BuildTarget target)
        {
            switch (compatibilityLevel)
            {
                case ApiCompatibilityLevel.NET_2_0_Subset:
                    return "legacyunity";

                case ApiCompatibilityLevel.NET_2_0:
                    return "net20";

                case ApiCompatibilityLevel.NET_Unity_4_8:
                case ApiCompatibilityLevel.NET_Standard:
                    return "unityaot-" + BuildTargetDiscovery.GetPlatformProfileSuffix(target);

                default:
                    throw new NotSupportedException(string.Format("ApiCompatibilityLevel.{0} is not supported by IL2CPP!", compatibilityLevel));
            }
        }

        internal static bool UseIl2CppCodegenWithMonoBackend(NamedBuildTarget namedBuildTarget)
        {
            return EditorApplication.useLibmonoBackendForIl2cpp &&
                PlayerSettings.GetScriptingBackend(namedBuildTarget) == ScriptingImplementation.IL2CPP;
        }

        internal static bool EnableIL2CPPDebugger(IIl2CppPlatformProvider provider)
        {
            if (!provider.allowDebugging || !provider.development)
                return false;

            switch (PlayerSettings.GetApiCompatibilityLevel(provider.namedBuildTarget))
            {
                case ApiCompatibilityLevel.NET_Unity_4_8:
                case ApiCompatibilityLevel.NET_Standard:
                    return true;

                default:
                    return false;
            }
        }

        internal static string[] GetBuilderDefinedDefines(IIl2CppPlatformProvider il2cppPlatformProvider)
        {
            List<string> defines = new List<string>();
            var apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(il2cppPlatformProvider.namedBuildTarget);

            switch (apiCompatibilityLevel)
            {
                case ApiCompatibilityLevel.NET_2_0:
                case ApiCompatibilityLevel.NET_2_0_Subset:
                    defines.AddRange(BaseDefines20);
                    break;

                case ApiCompatibilityLevel.NET_Unity_4_8:
                case ApiCompatibilityLevel.NET_Standard:
                    defines.AddRange(BaseDefines46);
                    break;

                default:
                    throw new InvalidOperationException($"IL2CPP doesn't support building with {apiCompatibilityLevel} API compatibility level!");
            }


            var target = il2cppPlatformProvider.target;
            if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64 ||
                target == BuildTarget.XboxOne || target == BuildTarget.WSAPlayer)
            {
                defines.AddRange(BaseDefinesWindows);

                if (target == BuildTarget.WSAPlayer)
                {
                    defines.Add("WINAPI_FAMILY=WINAPI_FAMILY_APP");
                }
                else
                {
                    defines.Add("WINAPI_FAMILY=WINAPI_FAMILY_DESKTOP_APP");
                }
            }

            if (EnableIL2CPPDebugger(il2cppPlatformProvider))
                defines.Add("IL2CPP_MONO_DEBUGGER=1");

            if (BuildPipeline.IsFeatureSupported("ENABLE_SCRIPTING_GC_WBARRIERS", target))
            {
                var hasGCBarrierValidation = PlayerSettings.gcWBarrierValidation;
                if (hasGCBarrierValidation)
                {
                    defines.Add("IL2CPP_ENABLE_STRICT_WRITE_BARRIERS=1");
                    defines.Add("IL2CPP_ENABLE_WRITE_BARRIER_VALIDATION=1");
                }

                var hasIncrementalGCTimeSlice = PlayerSettings.gcIncremental && (apiCompatibilityLevel == ApiCompatibilityLevel.NET_4_6 || apiCompatibilityLevel == ApiCompatibilityLevel.NET_Standard_2_0 ||
                    apiCompatibilityLevel == ApiCompatibilityLevel.NET_Unity_4_8 || apiCompatibilityLevel == ApiCompatibilityLevel.NET_Standard);

                if (hasGCBarrierValidation || hasIncrementalGCTimeSlice)
                {
                    var timeslice = hasIncrementalGCTimeSlice ? "3" : "0";
                    defines.Add("IL2CPP_ENABLE_WRITE_BARRIERS=1");
                    defines.Add($"IL2CPP_INCREMENTAL_TIME_SLICE={timeslice}");
                }
            }

            return defines.ToArray();
        }

        internal static string[] GetDebuggerIL2CPPArguments(IIl2CppPlatformProvider il2cppPlatformProvider)
        {
            var arguments = new List<string>();

            if (EnableIL2CPPDebugger(il2cppPlatformProvider))
                arguments.Add("--enable-debugger");

            return arguments.ToArray();
        }

        internal static string[] GetBuildingIL2CPPArguments(IIl2CppPlatformProvider il2cppPlatformProvider)
        {
            // When changing this function, don't forget to change GetBuilderDefinedDefines!
            var arguments = new List<string>();
            var apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(il2cppPlatformProvider.namedBuildTarget);

            if (BuildPipeline.IsFeatureSupported("ENABLE_SCRIPTING_GC_WBARRIERS", il2cppPlatformProvider.target))
            {
                var hasGCBarrierValidation = PlayerSettings.gcWBarrierValidation;
                if (hasGCBarrierValidation)
                    arguments.Add("--write-barrier-validation");

                var hasIncrementalGCTimeSlice = PlayerSettings.gcIncremental && (apiCompatibilityLevel == ApiCompatibilityLevel.NET_4_6 || apiCompatibilityLevel == ApiCompatibilityLevel.NET_Standard_2_0 ||
                    apiCompatibilityLevel == ApiCompatibilityLevel.NET_Unity_4_8 || apiCompatibilityLevel == ApiCompatibilityLevel.NET_Standard);
                if (hasIncrementalGCTimeSlice)
                    arguments.Add("--incremental-g-c-time-slice=3");
            }

            if (!string.IsNullOrEmpty(il2cppPlatformProvider.baselibLibraryDirectory))
                arguments.Add($"--baselib-directory=\"{il2cppPlatformProvider.baselibLibraryDirectory}\"");

            arguments.Add("--avoid-dynamic-library-copy");

            //make il2cpp not deploy baselib, as we already copy it in ourselves
            arguments.Add("--dont-deploy-baselib");

            arguments.Add("--profiler-report");

            return arguments.ToArray();
        }

        internal static string GetIl2CppFolder()
        {
            var pathOverride = System.Environment.GetEnvironmentVariable("UNITY_IL2CPP_PATH");
            if (!string.IsNullOrEmpty(pathOverride))
                return pathOverride;

            pathOverride = Debug.GetDiagnosticSwitch("VMIl2CppPath").value as string;
            if (!string.IsNullOrEmpty(pathOverride))
                return pathOverride;

            return Path.GetFullPath(Path.Combine(
                EditorApplication.applicationContentsPath,
                "il2cpp"));
        }

        internal static string GetIl2CppBeeSettingsFolder()
        {
            return $"{GetIl2CppFolder()}/build/BeeSettings/offline";
        }

        internal static string GetExePath(string executableFileName)
        {
            var platform = Application.platform;
            var deployDirectory = $"{IL2CPPUtils.GetIl2CppFolder()}/build/deploy";
            var il2cppPath = $"{deployDirectory}/{executableFileName}{(platform == RuntimePlatform.WindowsEditor ? ".exe" : "")}";
            if (!File.Exists(il2cppPath))
                il2cppPath = $"{deployDirectory}/{ExpectedTargetFrameworkDirectoryNameWhenUsingCustomBuild(platform)}/{BinaryDirectoryForPlatform(platform)}/{executableFileName}{(platform == RuntimePlatform.WindowsEditor ? ".exe" : "")}";
            return il2cppPath;
        }

        internal static string GetTundraFolder()
        {
            return $"{GetIl2CppFolder()}/external/bee/tundra";
        }

        internal static string GetReapiCacheClientFolder()
        {
            return $"{GetIl2CppFolder()}/external/bee/reapi-cache-client";
        }

        internal static string GetAdditionalArguments()
        {
            var arguments = new List<string>();
            var additionalArgs = PlayerSettings.GetAdditionalIl2CppArgs();
            if (!string.IsNullOrEmpty(additionalArgs))
                arguments.Add(additionalArgs);

            additionalArgs = System.Environment.GetEnvironmentVariable("IL2CPP_ADDITIONAL_ARGS");
            if (!string.IsNullOrEmpty(additionalArgs))
            {
                arguments.Add(additionalArgs);
            }

            additionalArgs = Debug.GetDiagnosticSwitch("VMIl2CppAdditionalArgs").value as string;
            if (!string.IsNullOrEmpty(additionalArgs))
            {
                arguments.Add(additionalArgs.Trim('\''));
            }

            return arguments.Aggregate(String.Empty, (current, arg) => current + arg + " ");
        }

        private static string BinaryDirectoryForPlatform(RuntimePlatform platform)
        {
            if (platform == RuntimePlatform.WindowsEditor)
                return "win-x64";
            else if (platform == RuntimePlatform.LinuxEditor)
                return "linux-x64";

            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
            if (platform == RuntimePlatform.OSXEditor && arch == "arm64")
                return "osx-arm64";
            return "osx-x64";
        }

        private static string ExpectedTargetFrameworkDirectoryNameWhenUsingCustomBuild(RuntimePlatform platform)
        {
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
            if (platform == RuntimePlatform.OSXEditor && arch == "arm64")
                return "net6.0";
            return "net5.0";
        }
    }

    internal class IL2CPPBuilder
    {
        private readonly string m_TempFolder;
        private readonly string m_StagingAreaData;
        private readonly IIl2CppPlatformProvider m_PlatformProvider;
        private readonly Action<string> m_ModifyOutputBeforeCompile;
        private readonly RuntimeClassRegistry m_RuntimeClassRegistry;
        private readonly bool m_BuildForMonoRuntime;

        public IL2CPPBuilder(string tempFolder, string stagingAreaData, IIl2CppPlatformProvider platformProvider, Action<string> modifyOutputBeforeCompile, RuntimeClassRegistry runtimeClassRegistry, bool buildForMonoRuntime)
        {
            m_TempFolder = tempFolder;
            m_StagingAreaData = stagingAreaData;
            m_PlatformProvider = platformProvider;
            m_ModifyOutputBeforeCompile = modifyOutputBeforeCompile;
            m_RuntimeClassRegistry = runtimeClassRegistry;
            m_BuildForMonoRuntime = buildForMonoRuntime;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetShortPathName", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int WindowsGetShortPathName(
            [MarshalAs(UnmanagedType.LPWStr)]
            string lpszLongPath,
            [MarshalAs(UnmanagedType.LPWStr)]
            StringBuilder lpszShortPath,
            int cchBuffer
        );

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        public static string GetShortPathName(string path)
        {
            if (!IsWindows() || Encoding.UTF8.GetByteCount(path) == path.Length)
            {
                return path;
            }
            int length = WindowsGetShortPathName(path, null, 0);
            if (length == 0)
            {
                return path;
            }
            StringBuilder shortPath = new StringBuilder(length);
            length = WindowsGetShortPathName(path, shortPath, shortPath.Capacity);
            if (length == 0)
            {
                return path;
            }
            return shortPath.ToString(0, length);
        }

        public void Run()
        {
            var generatedCppDir = GetCppOutputDirectory(m_PlatformProvider.il2cppBuildCacheDirectory);
            var additionalCppFilesDirectory = GetAdditionalCppFilesDirectory(m_PlatformProvider.il2cppBuildCacheDirectory);
            var managedDir = Path.GetFullPath(Path.Combine(m_StagingAreaData, "Managed"));

            // Make all assemblies in Staging/Managed writable for stripping.
            ClearReadOnlyFlagOnAllFilesNonRecursively(managedDir);

            var managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(m_PlatformProvider.namedBuildTarget);

            // IL2CPP does not support a managed stripping level of disabled. If the player settings
            // do try this (which should not be possible from the editor), use Low instead.
            if (managedStrippingLevel == ManagedStrippingLevel.Disabled)
                managedStrippingLevel = ManagedStrippingLevel.Minimal;
            AssemblyStripper.StripAssemblies(managedDir, m_PlatformProvider.CreateUnityLinkerPlatformProvider(), m_PlatformProvider, m_RuntimeClassRegistry, managedStrippingLevel);

            Directory.CreateDirectory(m_TempFolder);
            Directory.CreateDirectory(generatedCppDir);
            ClearReadOnlyFlagOnAllFilesNonRecursively(generatedCppDir);

            // Need to clean out the AdditionalCppFiles directory because a platform could do pretty much anything
            // in a "modifyOutputBeforeCompile" callback method, and so we need to provide a fresh directory. Bee will
            // still check the hash of these files before doing a build, so writing them again won't cause a recompile.
            if (Directory.Exists(additionalCppFilesDirectory))
            {
                ClearReadOnlyFlagOnAllFilesNonRecursively(additionalCppFilesDirectory);
                Directory.Delete(additionalCppFilesDirectory, true);
            }

            Directory.CreateDirectory(additionalCppFilesDirectory);

            if (m_ModifyOutputBeforeCompile != null)
                m_ModifyOutputBeforeCompile(additionalCppFilesDirectory);

            var pipelineData = new Il2CppBuildPipelineData(m_PlatformProvider.target, managedDir);

            ConvertPlayerDlltoCpp(pipelineData);
        }

        private static void ClearReadOnlyFlagOnAllFilesNonRecursively(string managedDir)
        {
            foreach (var file in Directory.GetFiles(managedDir))
            {
                var fileInfo = new FileInfo(file);
                fileInfo.IsReadOnly = false;
            }
        }

        public void RunCompileAndLink(string il2cppBuildCacheSource)
        {
            if (string.Equals(Path.GetFullPath(il2cppBuildCacheSource), Path.GetFullPath(m_PlatformProvider.il2cppBuildCacheDirectory), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("IIl2CppPlatformProvider.il2cppBuildCacheDirectory cannot be the same as il2cppBuildCacheDirectory. You probably forgot to override il2cppBuildCacheDirectory on your IIl2CppPlatformProvider.");

            var il2CppNativeCodeBuilder = m_PlatformProvider.CreateIl2CppNativeCodeBuilder();
            if (il2CppNativeCodeBuilder != null)
            {
                Il2CppNativeCodeBuilderUtils.ClearAndPrepareCacheDirectory(il2CppNativeCodeBuilder);

                var buildCacheDirectory = m_PlatformProvider.il2cppBuildCacheDirectory;
                Directory.CreateDirectory(buildCacheDirectory);

                var buildCacheNativeOutputFile = Path.Combine(GetNativeOutputRelativeDirectory(buildCacheDirectory), m_PlatformProvider.nativeLibraryFileName);
                var compilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(m_PlatformProvider.namedBuildTarget);
                var arguments = Il2CppNativeCodeBuilderUtils.AddBuilderArguments(il2CppNativeCodeBuilder, buildCacheNativeOutputFile, m_PlatformProvider.includePaths, m_PlatformProvider.libraryPaths, compilerConfiguration).ToList();

                var additionalArgs = IL2CPPUtils.GetAdditionalArguments();
                if (!string.IsNullOrEmpty(additionalArgs))
                    arguments.Add(additionalArgs);

                foreach (var buildingArgument in IL2CPPUtils.GetBuildingIL2CPPArguments(m_PlatformProvider))
                {
                    if (!arguments.Contains(buildingArgument))
                        arguments.Add(buildingArgument);
                }

                arguments.Add($"--generatedcppdir={CommandLineFormatter.PrepareFileName(GetCppOutputDirectory(il2cppBuildCacheSource))}");
                arguments.Add($"--dotnetprofile=\"{IL2CPPUtils.ApiCompatibilityLevelToDotNetProfileArgument(PlayerSettings.GetApiCompatibilityLevel(m_PlatformProvider.namedBuildTarget), m_PlatformProvider.target)}\"");
                arguments.AddRange(IL2CPPUtils.GetDebuggerIL2CPPArguments(m_PlatformProvider));
                Action<ProcessStartInfo> setupStartInfo = il2CppNativeCodeBuilder.SetupStartInfo;

                RunIl2CppWithArguments(arguments, setupStartInfo);
            }
        }

        private static string GetNativeOutputRelativeDirectory(string directory)
        {
            return Path.Combine(directory, "Native");
        }

        public static string GetAdditionalCppFilesDirectory(string directory)
        {
            return Path.Combine(GetShortPathName(Path.GetFullPath(directory)), "additionalCppFiles");
        }

        public static string GetCppOutputDirectory(string directory)
        {
            return Path.Combine(GetShortPathName(Path.GetFullPath(directory)), "il2cppOutput");
        }

        private void ConvertPlayerDlltoCpp(Il2CppBuildPipelineData data)
        {
            var arguments = new List<string>();

            arguments.Add("--convert-to-cpp");

            if (m_PlatformProvider.emitNullChecks)
                arguments.Add("--emit-null-checks");

            if (m_PlatformProvider.enableStackTraces)
                arguments.Add("--enable-stacktrace");

            if (m_PlatformProvider.enableArrayBoundsCheck)
                arguments.Add("--enable-array-bounds-check");

            if (m_PlatformProvider.enableDivideByZeroCheck)
                arguments.Add("--enable-divide-by-zero-check");

            if (m_PlatformProvider.development && m_PlatformProvider.enableDeepProfilingSupport)
                arguments.Add("--enable-deep-profiler");

            if (m_BuildForMonoRuntime)
                arguments.Add("--mono-runtime");

            var apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(m_PlatformProvider.namedBuildTarget);
            arguments.Add(string.Format("--dotnetprofile=\"{0}\"", IL2CPPUtils.ApiCompatibilityLevelToDotNetProfileArgument(apiCompatibilityLevel, m_PlatformProvider.target)));

            var il2cppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(m_PlatformProvider.namedBuildTarget);
            if (il2cppCodeGeneration == Il2CppCodeGeneration.OptimizeSize)
                arguments.Add("--generics-option=EnableFullSharing");

            var il2CppNativeCodeBuilder = m_PlatformProvider.CreateIl2CppNativeCodeBuilder();
            if (il2CppNativeCodeBuilder != null)
            {
                var buildCacheNativeOutputFile = Path.Combine(GetNativeOutputRelativeDirectory(m_PlatformProvider.il2cppBuildCacheDirectory), m_PlatformProvider.nativeLibraryFileName);
                var compilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(m_PlatformProvider.namedBuildTarget);
                Il2CppNativeCodeBuilderUtils.ClearAndPrepareCacheDirectory(il2CppNativeCodeBuilder);
                arguments.AddRange(Il2CppNativeCodeBuilderUtils.AddBuilderArguments(il2CppNativeCodeBuilder, buildCacheNativeOutputFile, m_PlatformProvider.includePaths, m_PlatformProvider.libraryPaths, compilerConfiguration));
            }
            else
            {
                arguments.Add($"--cachedirectory={CommandLineFormatter.PrepareFileName(GetShortPathName(Path.GetFullPath(m_PlatformProvider.il2cppBuildCacheDirectory)))}");
            }

            // Additional files can take any form, depending on platform, so pass anything in the additional files directory
            foreach (var additionalCppFile in Directory.GetFiles(GetAdditionalCppFilesDirectory(m_PlatformProvider.il2cppBuildCacheDirectory)))
                arguments.Add($"--additional-cpp={CommandLineFormatter.PrepareFileName(GetShortPathName(Path.GetFullPath(additionalCppFile)))}");

            arguments.AddRange(IL2CPPUtils.GetDebuggerIL2CPPArguments(m_PlatformProvider));
            foreach (var buildingArgument in IL2CPPUtils.GetBuildingIL2CPPArguments(m_PlatformProvider))
            {
                if (!arguments.Contains(buildingArgument))
                    arguments.Add(buildingArgument);
            }

            if (CrashReportingSettings.enabled)
                arguments.Add($"--emit-source-mapping");

            var additionalArgs = IL2CPPUtils.GetAdditionalArguments();
            if (!string.IsNullOrEmpty(additionalArgs))
                arguments.Add(additionalArgs);

            arguments.Add($"--directory={CommandLineFormatter.PrepareFileName(GetShortPathName(Path.GetFullPath(data.inputDirectory)))}");
            arguments.Add($"--generatedcppdir={CommandLineFormatter.PrepareFileName(GetCppOutputDirectory(m_PlatformProvider.il2cppBuildCacheDirectory))}");

            // NOTE: any arguments added here that affect how generated code is built need
            // to also be added to PlatformDependent\Win\Extensions\Managed\VisualStudioProjectHelpers.cs
            // as that file generated project files that invoke back into IL2CPP in order to build
            // generated code

            string progressMessage = "Converting managed assemblies to C++";
            if (il2CppNativeCodeBuilder != null)
            {
                progressMessage = "Building native binary with IL2CPP...";
            }

            if (EditorUtility.DisplayCancelableProgressBar("Building Player", progressMessage, 0.3f))
                throw new OperationCanceledException();

            Action<ProcessStartInfo> setupStartInfo = null;
            if (il2CppNativeCodeBuilder != null)
                setupStartInfo = il2CppNativeCodeBuilder.SetupStartInfo;

            if (PlayerBuildInterface.ExtraTypesProvider != null)
            {
                var extraTypes = new HashSet<string>();
                foreach (var extraType in PlayerBuildInterface.ExtraTypesProvider())
                {
                    extraTypes.Add(extraType);
                }

                var tempFile = Path.GetFullPath(Path.Combine(m_TempFolder, "extra-types.txt"));
                File.WriteAllLines(tempFile, extraTypes.ToArray());
                arguments.Add($"--extra-types-file={CommandLineFormatter.PrepareFileName(tempFile)}");
            }

            RunIl2CppWithArguments(arguments, setupStartInfo);
        }

        private void RunIl2CppWithArguments(List<string> arguments, Action<ProcessStartInfo> setupStartInfo)
        {
            var cppOutputDirectoryInBuildCache = GetCppOutputDirectory(m_PlatformProvider.il2cppBuildCacheDirectory);
            var cppOutputDirectoryInStagingArea = GetCppOutputDirectory(m_TempFolder);
            var nativeOutputDirectoryInBuildCache = GetNativeOutputRelativeDirectory(m_PlatformProvider.il2cppBuildCacheDirectory);
            var nativeOutputDirectoryInStagingArea = GetNativeOutputRelativeDirectory(m_StagingAreaData);

            var il2cppOutputParser = new Il2CppOutputParser(Path.Combine(cppOutputDirectoryInBuildCache, "Il2CppToEditorData.json"));

            arguments.Add("--convert-in-graph");

            var args = arguments.Aggregate(String.Empty, (current, arg) => current + arg + " ");
            Console.WriteLine("Invoking il2cpp with arguments: " + args);
            Runner.RunNetCoreProgram(GetIl2CppExe(), args, m_PlatformProvider.il2cppBuildCacheDirectory, il2cppOutputParser, setupStartInfo);

            // Copy IL2CPP outputs to StagingArea
            if (Directory.Exists(nativeOutputDirectoryInBuildCache))
                FileUtil.CopyDirectoryRecursive(nativeOutputDirectoryInBuildCache, nativeOutputDirectoryInStagingArea, true);

            // Copy Generated C++ files and Data directory to StagingArea.
            // This directory will only be present when using "--convert-to-cpp" with IL2CPP.
            if (Directory.Exists(cppOutputDirectoryInBuildCache))
            {
                FileUtil.CreateOrCleanDirectory(cppOutputDirectoryInStagingArea);
                FileUtil.CopyDirectoryRecursive(cppOutputDirectoryInBuildCache, cppOutputDirectoryInStagingArea);
            }
        }

        private string EscapeSpacesInPath(string path)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                return CommandLineFormatter.EscapeCharsWindows(path);
            return CommandLineFormatter.EscapeCharsQuote(path);
        }

        public static string GetIl2CppExe()
        {
            return IL2CPPUtils.GetExePath("il2cpp");
        }

        private string GetIl2CppTundraExe()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return $"{IL2CPPUtils.GetTundraFolder()}/tundra-mac-x64/tundra2";
            if (Application.platform == RuntimePlatform.LinuxEditor)
                return $"{IL2CPPUtils.GetTundraFolder()}/tundra-linux-x64/tundra2";

            return $"{IL2CPPUtils.GetTundraFolder()}/tundra-win-x64/tundra2.exe";
        }

        private string GetIl2CppReapiCacheClientExe()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return $"{IL2CPPUtils.GetReapiCacheClientFolder()}/tundra-mac-x64/tundra2";
            if (Application.platform == RuntimePlatform.LinuxEditor)
                return $"{IL2CPPUtils.GetReapiCacheClientFolder()}/tundra-linux-x64/tundra2";

            return $"{IL2CPPUtils.GetReapiCacheClientFolder()}/tundra-win-x64/tundra2.exe";
        }

        private string GetMonoBleedingEdgeExe()
        {
            var path = Path.Combine(MonoInstallationFinder.GetMonoInstallation("MonoBleedingEdge"), "bin");
            return Path.Combine(path, "mono");
        }
    }

    internal interface IIl2CppPlatformProvider
    {
        BuildTarget target { get; }
        NamedBuildTarget namedBuildTarget { get; }
        bool emitNullChecks { get; }
        bool enableStackTraces { get; }
        bool enableArrayBoundsCheck { get; }
        bool enableDivideByZeroCheck { get; }
        bool enableDeepProfilingSupport { get; }
        string nativeLibraryFileName { get; }
        bool supportsManagedDebugging { get; }
        bool development { get; }
        bool allowDebugging { get; }
        bool scriptsOnlyBuild { get; }
        string baselibLibraryDirectory { get; }
        string il2cppBuildCacheDirectory { get; }

        BuildReport buildReport { get; }
        string[] includePaths { get; }
        string[] libraryPaths { get; }

        INativeCompiler CreateNativeCompiler();
        Il2CppNativeCodeBuilder CreateIl2CppNativeCodeBuilder();

        BaseUnityLinkerPlatformProvider CreateUnityLinkerPlatformProvider();
    }

    internal abstract class BaseIl2CppPlatformProvider : IIl2CppPlatformProvider
    {
        private string _baselibLibraryDirectory;

        public BaseIl2CppPlatformProvider(BuildTarget target, string libraryFolder, BuildReport buildReport,
                                          string baselibLibraryDirectory)
        {
            this.target = target;
            this.namedBuildTarget = NamedBuildTarget.FromActiveSettings(target);
            this.libraryFolder = libraryFolder;
            this.buildReport = buildReport;
            _baselibLibraryDirectory = baselibLibraryDirectory;
        }

        public virtual BuildTarget target { get; private set; }

        public virtual NamedBuildTarget namedBuildTarget { get; private set; }

        public virtual string libraryFolder { get; private set; }

        public virtual bool emitNullChecks
        {
            get { return true; }
        }

        // This is an opt-in setting, as most platforms will want to use native stacktrace mechanisms
        public virtual bool enableStackTraces
        {
            get { return false; }
        }

        public virtual bool enableArrayBoundsCheck
        {
            get { return true; }
        }

        public virtual bool enableDivideByZeroCheck
        {
            get { return false; }
        }

        public virtual bool enableDeepProfilingSupport
        {
            get
            {
                if (buildReport != null)
                    return (buildReport.summary.options & BuildOptions.EnableDeepProfilingSupport) == BuildOptions.EnableDeepProfilingSupport;
                return false;
            }
        }

        public virtual bool supportsManagedDebugging
        {
            get { return false; }
        }

        public virtual bool development
        {
            get
            {
                if (buildReport != null)
                    return (buildReport.summary.options & BuildOptions.Development) == BuildOptions.Development;
                return false;
            }
        }

        public virtual bool allowDebugging
        {
            get
            {
                if (buildReport != null)
                    return (buildReport.summary.options & BuildOptions.AllowDebugging) == BuildOptions.AllowDebugging;
                return false;
            }
        }

        public virtual bool scriptsOnlyBuild
        {
            get
            {
                if (buildReport != null)
                    return (buildReport.summary.options & BuildOptions.BuildScriptsOnly) == BuildOptions.BuildScriptsOnly;
                return false;
            }
        }

        public string baselibLibraryDirectory
        {
            get { return _baselibLibraryDirectory; }
        }

        public virtual string il2cppBuildCacheDirectory
        {
            get { return $"Library/Il2cppBuildCache/{target}"; }
        }

        public BuildReport buildReport { get; private set; }

        public virtual string[] includePaths
        {
            get
            {
                return new[]
                {
                    Path.Combine(libraryFolder, "bdwgc/include"),
                    Path.Combine(libraryFolder, "libil2cpp/include")
                };
            }
        }

        public virtual string[] libraryPaths
        {
            get
            {
                return new string[0];
            }
        }

        public virtual string nativeLibraryFileName
        {
            get { return null; }
        }

        public virtual INativeCompiler CreateNativeCompiler()
        {
            return null;
        }

        public virtual Il2CppNativeCodeBuilder CreateIl2CppNativeCodeBuilder()
        {
            return null;
        }

        public abstract BaseUnityLinkerPlatformProvider CreateUnityLinkerPlatformProvider();
    }
}
