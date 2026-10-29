using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace FieldInspection.Editor
{
    public static class AndroidSetup
    {
        const string Scene = "Assets/Scenes/Inspection.unity";

        [MenuItem("Inspection/Configure Android ARCore")]
        public static void Configure()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Install Android Build Support in Unity Hub first.");
            PlayerSettings.companyName = "FieldInspection";
            PlayerSettings.productName = "Fieldnote AR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.fieldinspection.prototype");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            // Legacy Input + StandaloneInputModule is intentional; no dual-input ambiguity.
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = playerSettings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; playerSettings.ApplyModifiedProperties(); }

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget platforms) || platforms == null)
            {
                var existing = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (existing.Length > 0)
                    platforms = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(existing[0]));
                else
                {
                    platforms = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                    AssetDatabase.CreateAsset(platforms, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, platforms, true);
            }
            if (!platforms.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                platforms.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!platforms.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                platforms.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = platforms.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not enable ARCore; enable it in XR Plug-in Management manually.");
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(platforms);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Scene);
            Debug.Log("Configured. Run XR Plug-in Management > Project Validation before building. Allow any requested editor restart.");
        }

        [MenuItem("Inspection/Build Android APK")]
        public static void Build()
        {
            Configure();
            System.IO.Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Scene }, locationPathName = "Builds/FieldnoteAR.apk",
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Android build failed: " + report.summary.result);
        }
    }
}
