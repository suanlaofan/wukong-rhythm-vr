using UnityEngine;

/// <summary>Bounded decorative effects: no rigidbodies, transient lights or per-hit materials.</summary>
public sealed class WukongHitEffectPool : MonoBehaviour
{
    private readonly ParticleSystem[] effects = new ParticleSystem[16];
    private int next;
    public void Initialize(Material material)
    {
        GameObject meshSource = GameObject.CreatePrimitive(PrimitiveType.Cube);
        meshSource.SetActive(false);
        Mesh sparkMesh = meshSource.GetComponent<MeshFilter>().sharedMesh;
        Destroy(meshSource);
        for (int i = 0; i < effects.Length; i++)
        {
            GameObject root = new GameObject("Pooled Rhythm Sparks"); root.transform.SetParent(transform, false);
            ParticleSystem ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false;
            main.duration = 0.7f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 32;
            var emission = ps.emission; emission.rateOverTime = 0;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.08f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = sparkMesh;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            effects[i] = ps;
        }
    }
    public void SetPaused(bool paused)
    {
        foreach (var ps in effects) if (ps != null)
        {
            if (paused && ps.isPlaying) ps.Pause();
            else if (!paused && ps.isPaused) ps.Play();
        }
    }
    public void Clear()
    { foreach (var ps in effects) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }

    public void Emit(Vector3 position, bool powerful, bool miss)
    {
        ParticleSystem ps = effects[next]; next = (next + 1) % effects.Length;
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.transform.position = position;
        var main = ps.main;
        main.startColor = miss ? new Color(0.24f, 0.18f, 0.2f, 0.55f)
            : powerful ? new Color(1f, 0.45f, 0.08f) : new Color(0.5f, 0.95f, 1f);
        ps.Play(); ps.Emit(miss ? 4 : powerful ? 24 : 16);
    }
}
