using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class ShellProbe : MonoBehaviour
{
    public Renderer bcCube;
    public Renderer resourceCube;
    public Material invertedMaterial;

    int frames;

    void Start()
    {
        Debug.Log($"SHELL_START platform={Application.platform} unity={Application.unityVersion} " +
                  $"gfx={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName} " +
                  $"bc7={SystemInfo.IsFormatSupported(GraphicsFormat.RGBA_BC7_UNorm, GraphicsFormatUsage.Sample)} " +
                  $"dxt5={SystemInfo.IsFormatSupported(GraphicsFormat.RGBA_DXT5_UNorm, GraphicsFormatUsage.Sample)}");

        Report("bcCube", bcCube.sharedMaterial);

        var resTex = Resources.Load<Texture2D>("ResTex");
        Debug.Log(resTex == null
            ? "SHELL_RESOURCE missing"
            : $"SHELL_RESOURCE name={resTex.name} format={resTex.graphicsFormat} size={resTex.width}x{resTex.height}");
        if (resTex != null)
        {
            var m = new Material(bcCube.sharedMaterial) { mainTexture = resTex };
            resourceCube.sharedMaterial = m;
        }

        Report("inverted", invertedMaterial);
    }

    static void Report(string label, Material m)
    {
        var tex = m.mainTexture as Texture2D;
        Debug.Log($"SHELL_MATERIAL {label} shader={m.shader.name} supported={m.shader.isSupported} " +
                  $"keywords=[{string.Join(",", m.shaderKeywords)}] " +
                  $"tex={(tex == null ? "null" : tex.name + ":" + tex.graphicsFormat)}");
    }

    void Update()
    {
        transform.Rotate(0f, 45f * Time.deltaTime, 0f);
        frames++;
        if (frames == 90)
            Debug.Log($"SHELL_DONE frames={frames} t={Time.realtimeSinceStartup:F1}s");
    }
}
