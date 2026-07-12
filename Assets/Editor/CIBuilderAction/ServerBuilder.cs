using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace CIBuilderAction
{
    public static class ServerBuilder
    {
        public static void BuildProject()
        {
            string workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? ".";
            string buildPath = Environment.GetEnvironmentVariable("BUILD_PATH") ?? "build";
            string buildFile = Environment.GetEnvironmentVariable("BUILD_FILE") ?? "Server";
            string profileAssetPath = "Assets/Settings/BuildProfiles/LinuxServer.asset";

            string fullBuildPath = Path.Combine(workspace, buildPath);
            Directory.CreateDirectory(fullBuildPath);
            string locationPathName = Path.Combine(fullBuildPath, buildFile);

            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profileAssetPath);
            if (profile == null)
            {
                Console.WriteLine($"##[error]Could not load build profile at path: {profileAssetPath}");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = locationPathName,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Console.WriteLine($"Build result: {summary.result}, Errors: {summary.totalErrors}, Warnings: {summary.totalWarnings}, Size: {summary.totalSize} bytes");

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}