// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

//Keep this namespace to be compatible with visual effect graph package 7.0.1
//There was an unexpected useless "using UnityEngine.Experimental.VFX;" in VFXMotionVector.cs
namespace UnityEngine.Experimental.VFX
{
    internal static class VFXManager
    {
    }
}

namespace UnityEngine.VFX
{
    [RequiredByNativeCode]
    public struct VFXCameraXRSettings
    {
        public uint viewTotal;
        public uint viewCount;
        public uint viewOffset;
    }

    [RequiredByNativeCode]
    [NativeHeader("Modules/VFX/Public/VFXManager.h")]
    [StaticAccessor("GetVFXManager()", StaticAccessorType.Dot)]
    public static class VFXManager
    {
        extern public static VisualEffect[] GetComponents();
        extern internal static ScriptableObject runtimeResources { get; }

        extern public static float fixedTimeStep { get; set; }
        extern public static float maxDeltaTime { get; set; }

        extern internal static float maxScrubTime { get; set; }
        extern internal static string renderPipeSettingsPath { get; }

        extern internal static void ResyncMaterials([NotNull("NullExceptionObject")] VisualEffectAsset asset);
        extern internal static bool renderInSceneView { get; set; }
        internal static bool activateVFX { get; set; }

        private static readonly VFXCameraXRSettings kDefaultCameraXRSettings = new VFXCameraXRSettings { viewTotal = 1, viewCount = 1, viewOffset = 0 };

        public static void ProcessCamera(Camera cam)
        {
            PrepareCamera(cam, kDefaultCameraXRSettings);
            ProcessCameraCommand(cam, null, kDefaultCameraXRSettings);
        }

        public static void PrepareCamera(Camera cam)
        {
            PrepareCamera(cam, kDefaultCameraXRSettings);
        }

        extern public static void PrepareCamera([NotNull("NullExceptionObject")] Camera cam, VFXCameraXRSettings camXRSettings);

        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd)
        {
            ProcessCameraCommand(cam, cmd, kDefaultCameraXRSettings);
        }

        extern public static void ProcessCameraCommand([NotNull("NullExceptionObject")] Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings);
        extern public static VFXCameraBufferTypes IsCameraBufferNeeded([NotNull("NullExceptionObject")] Camera cam);
        extern public static void SetCameraBuffer([NotNull("NullExceptionObject")] Camera cam, VFXCameraBufferTypes type, Texture buffer, int x, int y, int width, int height);
    }
}
