using System;
using System.IO;
using System.Linq;
using CasinoMares.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// Casino Mares ▸ Web: the browser build (WebGL) in <c>Builds/WebGL</c>. Desktop browsers load DXT textures. Phones
    /// and tablets can't decode those in hardware, so the full build runs twice and adds the ASTC textures as a second
    /// data file (<c>*_Mobile.data*</c>); the CasinoMares page template picks one when it loads. Both builds share the
    /// same code. Files are gzipped with decompression fallback, so any static host works.
    /// </summary>
    public static class WebBuild
    {
        const string OutputPath = "Builds/WebGL";
        const string MobilePassPath = "Temp/WebGL-Mobile";
        const string MobileSuffix = "_Mobile";
        const string PendingKey = "CasinoMares.WebBuild.Pending";

        [MenuItem("Casino Mares/Web/Build (Desktop and Mobile)", priority = 100)]
        static void BuildAllMenu() => Build(includeMobile: true);

        [MenuItem("Casino Mares/Web/Build Desktop Only (Faster)", priority = 101)]
        static void BuildDesktopMenu() => Build(includeMobile: false);

        [MenuItem("Casino Mares/Web/Simulate Touch Controls", priority = 200)]
        static void ToggleSimulateTouch()
        {
            EditorPrefs.SetBool(Platform.SimulateTouchKey, !EditorPrefs.GetBool(Platform.SimulateTouchKey, false));
        }

        [MenuItem("Casino Mares/Web/Simulate Touch Controls", true)]
        static bool ToggleSimulateTouchValidate()
        {
            Menu.SetChecked("Casino Mares/Web/Simulate Touch Controls", EditorPrefs.GetBool(Platform.SimulateTouchKey, false));
            return !EditorApplication.isPlaying;
        }

        /// <summary>Entry point for batch mode: <c>-executeMethod CasinoMares.Editor.WebBuild.BuildFromCommandLine</c>.</summary>
        public static void BuildFromCommandLine()
        {
            var ok = Build(includeMobile: !Environment.GetCommandLineArgs().Contains("-desktopOnly"));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build(bool includeMobile)
        {
            // The WebGL build pipeline only loads after the scripts reload on the new platform, so switch first and
            // carry on in ResumeAfterSwitch.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                SessionState.SetInt(PendingKey, includeMobile ? 2 : 1);
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    SessionState.EraseInt(PendingKey);
                    Debug.LogError("[WebBuild] Could not switch the build target to WebGL. Is the WebGL module installed?");
                    return false;
                }

                Debug.Log("[WebBuild] Switched to WebGL; the build starts once the scripts have reloaded.");
                return true;
            }

            ApplyPlayerSettings();
            var previousSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
            try
            {
                string mobileData = null;
                if (includeMobile)
                {
                    if (!BuildPass(MobilePassPath, WebGLTextureSubtarget.ASTC, "mobile (ASTC)"))
                        return false;
                    mobileData = FindDataFile(MobilePassPath);
                }

                if (!BuildPass(OutputPath, WebGLTextureSubtarget.DXT, "desktop (DXT)"))
                    return false;

                // A desktop-only build must not leave an older mobile data file around: it wouldn't match the new code.
                var buildFolder = Path.Combine(OutputPath, "Build");
                foreach (var stale in Directory.GetFiles(buildFolder, $"*{MobileSuffix}.data*"))
                    File.Delete(stale);

                if (mobileData != null)
                {
                    var desktopData = FindDataFile(OutputPath);
                    var name = Path.GetFileName(desktopData);
                    var dot = name.IndexOf(".data", StringComparison.Ordinal);
                    var mobileName = name.Substring(0, dot) + MobileSuffix + name.Substring(dot);
                    File.Copy(mobileData, Path.Combine(buildFolder, mobileName), true);
                }

                Debug.Log($"[WebBuild] Done: {Path.GetFullPath(OutputPath)}\n{Summary(buildFolder)}");
                return true;
            }
            finally
            {
                EditorUserBuildSettings.webGLBuildSubtarget = previousSubtarget;
            }
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterSwitch()
        {
            var pending = SessionState.GetInt(PendingKey, 0);
            if (pending == 0 || EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                return;

            SessionState.EraseInt(PendingKey);
            EditorApplication.delayCall += () => Build(includeMobile: pending == 2);
        }

        /// <summary>The browser build's player settings, kept here so every machine builds the same thing.</summary>
        static void ApplyPlayerSettings()
        {
            PlayerSettings.WebGL.template = "PROJECT:CasinoMares";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            // Tables keep running for the others while this tab is in the background (browsers slow it to ~1 fps).
            PlayerSettings.runInBackground = true;
        }

        static bool BuildPass(string path, WebGLTextureSubtarget textures, string label)
        {
            // The options' subtarget is what picks the texture format; the build settings value alone is ignored.
            EditorUserBuildSettings.webGLBuildSubtarget = textures;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                subtarget = (int)textures,
                options = BuildOptions.None,
            };

            Debug.Log($"[WebBuild] Building {label} into {path}...");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            // A failing post-process step can still report success, so check for the page itself.
            if (summary.result == BuildResult.Succeeded && File.Exists(Path.Combine(path, "index.html")))
            {
                Debug.Log($"[WebBuild] {label}: {summary.totalSize / (1024f * 1024f):0.0} MB in {summary.totalTime.TotalSeconds:0} s");
                return true;
            }

            Debug.LogError($"[WebBuild] {label} failed: {summary.result}, {summary.totalErrors} errors");
            return false;
        }

        static string FindDataFile(string buildPath)
        {
            return Directory.GetFiles(Path.Combine(buildPath, "Build"), "*.data*")
                .First(file => !Path.GetFileName(file).Contains(MobileSuffix));
        }

        static string Summary(string buildFolder)
        {
            return string.Join("\n", Directory.GetFiles(buildFolder)
                .OrderBy(file => file)
                .Select(file => $"  {Path.GetFileName(file)}: {new FileInfo(file).Length / (1024f * 1024f):0.00} MB"));
        }
    }
}
