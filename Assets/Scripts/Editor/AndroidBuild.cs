using System;
using System.IO;
using System.Linq;
using NuggetCreek.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using Unity.Notifications;
using UnityEngine;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// The one place Android builds are made: player settings, signing, version code and output.
    /// Settings live in code so a stray Inspector click cannot ship a wrong package.
    ///
    /// The release AAB is signed with the upload key in android-keystore/ (gitignored; backup in
    /// the private pictures repo). The password is read from NC_KEYSTORE_PASS, else
    /// android-keystore/nuggetcreek-upload.pass, and never enters the repo.
    /// Dev and measurement APKs stay on the debug key: switching keys on a test phone needs an
    /// uninstall, which also drops the AndroidKeyStore key that signs the save (SaveKey).
    ///
    /// Batchmode: -executeMethod NuggetCreek.Editor.AndroidBuild.{BuildDevApk|BuildMeasureApk|BuildReleaseAab|BumpPatch}
    /// </summary>
    public static class AndroidBuild
    {
        const string PackageName = "com.yilkgames.nuggetcreek";
        const string KeystorePath = "android-keystore/nuggetcreek-upload.jks";
        const string KeyAlias = "nuggetcreek";
        const string PassFile = "android-keystore/nuggetcreek-upload.pass";
        const string PassEnv = "NC_KEYSTORE_PASS";
        const string OutputDir = "Builds/Android";
        // Play requires API 36 for new apps and updates from 2026-08-31.
        const int TargetSdk = 36;

        /// <summary>Development APK for phone playtests (profiler, logs, debug buttons).</summary>
        [MenuItem("Nugget Creek/Android/Build Development APK")]
        public static void BuildDevApk() => Build(Variant.Dev);

        /// <summary>Store settings as an APK plus the PerfProbe log, for size, RAM and FPS checks.</summary>
        [MenuItem("Nugget Creek/Android/Build Measurement APK")]
        public static void BuildMeasureApk() => Build(Variant.Measure);

        /// <summary>Signed AAB for Play Console. Refuses to overwrite an existing version code.</summary>
        [MenuItem("Nugget Creek/Android/Build Release AAB")]
        public static void BuildReleaseAab() => Build(Variant.Release);

        /// <summary>Raises the patch number; every Play upload needs a higher version code.</summary>
        [MenuItem("Nugget Creek/Android/Bump Patch Version")]
        public static void BumpPatch()
        {
            AppVersion next = CurrentVersion().NextPatch();
            PlayerSettings.bundleVersion = next.ToString();
            PlayerSettings.Android.bundleVersionCode = next.VersionCode;
            AssetDatabase.SaveAssets();
            Debug.Log($"[AndroidBuild] version {next} code {next.VersionCode}");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        enum Variant { Dev, Measure, Release }

        static void Build(Variant variant)
        {
            bool ok = false;
            try
            {
                ok = TryBuild(variant);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidBuild] {e.Message}");
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool TryBuild(Variant variant)
        {
            AppVersion version = ApplyPlayerSettings();
            // Firebase's Android libraries enter through mainTemplate.gradle, patched by the
            // External Dependency Manager; batchmode never runs its auto-resolve, so force it.
            if (!GooglePlayServices.PlayServicesResolver.ResolveSync(true))
                throw new InvalidOperationException("Android dependency resolution failed");
            bool signed = ApplySigning(variant == Variant.Release);
            if (variant == Variant.Release && !signed)
                throw new InvalidOperationException($"release AAB needs the upload key password ({PassEnv} or {PassFile})");

            // A dirty open scene makes BuildPlayer show a modal that nobody can click in automation.
            EditorSceneManager.SaveOpenScenes();

            bool aab = variant == Variant.Release;
            EditorUserBuildSettings.buildAppBundle = aab;
            Directory.CreateDirectory(OutputDir);
            string suffix = variant == Variant.Release ? "" : "-" + variant.ToString().ToLowerInvariant();
            string path = $"{OutputDir}/NuggetCreek-{version}-{version.VersionCode}{suffix}{(aab ? ".aab" : ".apk")}";
            if (aab && File.Exists(path))
                throw new InvalidOperationException($"{path} exists; bump the version before a new upload");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                options = variant == Variant.Dev ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = variant == Variant.Measure ? new[] { "NC_PERF_PROBE" } : Array.Empty<string>(),
            };
            BuildSummary summary;
            try
            {
                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                // Leave the project on the debug key so editor Build And Run keeps working.
                PlayerSettings.Android.useCustomKeystore = false;
                AssetDatabase.SaveAssets();
            }
            long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
            Debug.Log($"[AndroidBuild] variant={variant} result={summary.result} errors={summary.totalErrors} " +
                      $"version={version} code={version.VersionCode} uploadKey={signed} " +
                      $"fileMb={bytes / (1024.0 * 1024.0):F1} path={path}");
            return summary.result == BuildResult.Succeeded;
        }

        static AppVersion ApplyPlayerSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            // Play wants 64-bit; ARM64 needs IL2CPP. ARMv7-only phones are outside the target market.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetSdk;
            // Local notifications (design doc 10.1.1 rule 6): inexact only, and the 30 day chain
            // must survive a phone restart.
            NotificationSettings.AndroidSettings.ExactSchedulingOption = 0;
            NotificationSettings.AndroidSettings.RescheduleOnDeviceRestart = true;

            AppVersion version = CurrentVersion();
            PlayerSettings.Android.bundleVersionCode = version.VersionCode;
            return version;
        }

        static AppVersion CurrentVersion() => AppVersion.Parse(PlayerSettings.bundleVersion);

        static bool ApplySigning(bool uploadKey)
        {
            PlayerSettings.Android.useCustomKeystore = false;
            if (!uploadKey)
                return false;
            string pass = Environment.GetEnvironmentVariable(PassEnv);
            if (string.IsNullOrEmpty(pass) && File.Exists(PassFile))
                pass = File.ReadAllText(PassFile).Trim();
            if (string.IsNullOrEmpty(pass) || !File.Exists(KeystorePath))
            {
                Debug.LogError($"[AndroidBuild] upload key not found ({KeystorePath}, {PassEnv} or {PassFile}).");
                return false;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Path.GetFullPath(KeystorePath);
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasName = KeyAlias;
            PlayerSettings.Android.keyaliasPass = pass;
            return true;
        }
    }
}
