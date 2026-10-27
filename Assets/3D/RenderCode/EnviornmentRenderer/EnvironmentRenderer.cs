using UnityEngine;

public class EnvironmentRenderer : MonoBehaviour
{
    [SerializeField] private Color tankColor = Color.white;

    private Mesh tankMesh;
    private Material material;
    private RenderParams rp;

    public void Setup(SimulationManager sim)
    {
        // Always-included built-in shader, so no material asset is needed
        material = material != null ? material : new Material(Shader.Find("Hidden/Internal-Colored"));
        rp = new RenderParams(material);

        Destroy(tankMesh);
        tankMesh = MeshGenerator.WireBox(sim.Tank.size);
    }

    public void DrawTank()
    {
        if (tankMesh == null) return;
        material.color = tankColor;
        Graphics.RenderMesh(rp, tankMesh, 0, Matrix4x4.identity);
    }

    private void OnDestroy()
    {
        Destroy(tankMesh);
        Destroy(material);
    }
}
