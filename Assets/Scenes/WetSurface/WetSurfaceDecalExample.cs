using Illusion.Rendering;
using UnityEngine;

[ExecuteAlways]
[AddComponentMenu("Illusion RP/Examples/Wet Surface Decal")]
public sealed class WetSurfaceDecalExample : MonoBehaviour, IWetSurfaceDecal
{
    public Texture2D mask;
    public Vector2 tiling = Vector2.one;
    public Vector2 offset;
    [Range(0, 1)] public float threshold;
    [Range(0.001f, 1)] public float softness = 1;
    [Range(0, 1)] public float saturation = 1;
    [Range(0.001f, 1)] public float edgeFade = 0.2f;
    [Min(0.001f)] public float faceSharpness = 1;
    public bool dry;
    public bool sphere;

    private void OnEnable() => WetSurfaceDecalRegistry.Register(this);

    private void OnDisable() => WetSurfaceDecalRegistry.Unregister(this);

    public bool TryGetWetSurfaceData(out WetSurfaceDecalData data)
    {
        var layer = new WetSurfaceDecalLayerData(mask, new Vector4(tiling.x, tiling.y, offset.x, offset.y),
            new Vector4(threshold, 0, 0, 0), new Vector4(softness, 1, 1, 1),
            Vector4.zero, new Vector4(1, 0, 0, 0));
        data = new WetSurfaceDecalData(transform.worldToLocalMatrix, transform.localToWorldMatrix,
            WetSurfaceLayerMode.Single, WetSurfaceProjectionMode.Local,
            default, layer, default, saturation, edgeFade, faceSharpness, false, 0, dry, sphere);
        return isActiveAndEnabled;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = dry ? new Color(1, 0.65f, 0.1f) : new Color(0.1f, 0.65f, 1);
        if (sphere)
            Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        else
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
