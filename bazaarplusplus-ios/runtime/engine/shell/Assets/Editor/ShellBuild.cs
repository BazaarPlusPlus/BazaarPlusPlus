using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShellBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string BcTexPath = "Assets/Textures/BcTex.png";
    const string ResTexPath = "Assets/Resources/ResTex.png";

    public static void Setup()
    {
        WriteChecker(BcTexPath, new Color(0.9f, 0.3f, 0.2f), Color.white);
        WriteChecker(ResTexPath, new Color(0.2f, 0.5f, 0.9f), Color.yellow);
        AssetDatabase.Refresh();
        ConfigureImporter(BcTexPath, TextureImporterFormat.BC7);
        ConfigureImporter(ResTexPath, TextureImporterFormat.DXT5);

        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/ShellUnlit.shader");
        var mat = new Material(shader) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BcTexPath) };
        AssetDatabase.CreateAsset(mat, "Assets/Textures/BcMat.mat");
        var inv = new Material(shader) { mainTexture = mat.mainTexture };
        inv.EnableKeyword("SHELL_INVERT");
        AssetDatabase.CreateAsset(inv, "Assets/Textures/InvMat.mat");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -6);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);

        var root = new GameObject("Root");
        var probe = root.AddComponent<ShellProbe>();
        probe.bcCube = Cube(root, "BcCube", new Vector3(-1.6f, 1.2f, 0), mat);
        probe.resourceCube = Cube(root, "ResourceCube", new Vector3(1.6f, 1.2f, 0), mat);
        Cube(root, "InvertedCube", new Vector3(0, -1.4f, 0), inv);
        probe.invertedMaterial = inv;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
    }

    public static void BuildMacRgba()
    {
        ConfigureImporter(BcTexPath, TextureImporterFormat.RGBA32);
        ConfigureImporter(ResTexPath, TextureImporterFormat.RGBA32);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
        Build(BuildTarget.StandaloneOSX, "Builds/MacRGBA/BppShell.app");
    }

    // Release player shell for running other games: a Development player makes Debug.isDebugBuild true,
    // which sends release-built packages (URP) down debug-only paths.
    public static void BuildIOSRelease()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.bazaarplusplus.ios.shell");
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
        PlayerSettings.stripEngineCode = false;
        Build(BuildTarget.iOS, "Builds/iOSRelease", BuildOptions.None);
    }

    // The export uses Unity's stock IL2CPP backend; tools/build.py swaps GameAssembly for static Mono.
    public static void BuildIOSDeviceRelease()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.bazaarplusplus.ios.shell");
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        PlayerSettings.stripEngineCode = false;
        Build(BuildTarget.iOS, "Builds/iOSDevice", BuildOptions.None);
    }

    static void Build(BuildTarget target, string path, BuildOptions options = BuildOptions.Development)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            options = options,
        });
        Debug.Log($"SHELL_BUILD {target} result={report.summary.result} errors={report.summary.totalErrors}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    static Renderer Cube(GameObject parent, string name, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        return r;
    }

    static void WriteChecker(string path, Color a, Color b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                tex.SetPixel(x, y, ((x / 32) + (y / 32)) % 2 == 0 ? a : b);
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    static void ConfigureImporter(string path, TextureImporterFormat standaloneFormat)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone", overridden = true, format = standaloneFormat, maxTextureSize = 256,
        });
        imp.SaveAndReimport();
    }
}
