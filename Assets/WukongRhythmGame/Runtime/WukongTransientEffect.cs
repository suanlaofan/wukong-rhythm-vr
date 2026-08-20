using UnityEngine;

public sealed class WukongTransientEffect : MonoBehaviour
{
    public float life = 1f;
    public bool fadeLight;

    private float remaining;
    private Light effectLight;
    private float initialIntensity;

    private void Start()
    {
        remaining = life;
        if (fadeLight)
        {
            effectLight = GetComponent<Light>();
            if (effectLight != null)
            {
                initialIntensity = effectLight.intensity;
            }
        }
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (effectLight != null)
        {
            effectLight.intensity = initialIntensity * Mathf.Clamp01(remaining / Mathf.Max(0.01f, life));
        }
        if (remaining <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
