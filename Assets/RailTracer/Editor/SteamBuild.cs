using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RailTracer
{
    // The Steam build: Windows 64-bit into Builds/Steam/Windows, the folder steam/app_build.vdf uploads.
    // steam/upload.ps1 runs it headless (Unity -batchmode -executeMethod RailTracer.SteamBuild.Build) and uploads.
    public static class SteamBuild
    {
        const string Folder = "Builds/Steam/Windows";

        [MenuItem("Rail Tracer/Build For Steam")]
        public static void Build()
        {
            if (Directory.Exists(Folder)) FileUtil.DeleteFileOrDirectory(Folder); // nothing stale gets shipped
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = $"{Folder}/{PlayerSettings.productName}.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"Steam build: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, into {Folder}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
