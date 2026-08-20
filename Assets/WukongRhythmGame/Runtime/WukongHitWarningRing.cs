using UnityEngine;

public sealed class WukongHitWarningRing : MonoBehaviour
{
    private const int SegmentCount = 48;
    private WukongRhythmGame game;
    private LineRenderer outerRing;
    private LineRenderer innerRing;
    private float targetSongTime;
    private float warningBeats;
    private bool resolved;
    private float resolveStarted;
    private Color resolveColor;

    public static WukongHitWarningRing Create(
        WukongRhythmGame owner,
        Vector3 position,
        float targetTime,
        float warningBeatCount,
        bool powerful)
    {
        GameObject ringObject = new GameObject("Rhythm Hit Warning Ring");
        ringObject.transform.SetParent(owner.transform, true);
        WukongHitWarningRing ring = ringObject.AddComponent<WukongHitWarningRing>();
        ring.Initialize(owner, position, targetTime, warningBeatCount, powerful);
        return ring;
    }

    private void Initialize(
        WukongRhythmGame owner,
        Vector3 position,
        float targetTime,
        float warningBeatCount,
        bool powerful)
    {
        game = owner;
        targetSongTime = targetTime;
        warningBeats = Mathf.Max(2f, warningBeatCount);
        transform.position = position;
        if (game.playerCamera != null)
        {
            transform.rotation = Quaternion.LookRotation(game.playerCamera.transform.forward, game.playerCamera.transform.up);
        }

        outerRing = CreateLine("Outer", powerful ? 0.022f : 0.016f);
        innerRing = CreateLine("Inner", powerful ? 0.014f : 0.009f);
        SetCircle(outerRing, 0.28f);
        SetCircle(innerRing, 0.175f);
        SetColor(new Color(0.35f, 0.92f, 1f, 0.06f));
    }

    private void Update()
    {
        if (resolved)
        {
            float fade = 1f - Mathf.Clamp01((Time.unscaledTime - resolveStarted) / 0.28f);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, resolveColor == Color.clear ? 1.45f : 1.2f, 1f - fade);
            SetColor(new Color(resolveColor.r, resolveColor.g, resolveColor.b, fade * 0.9f));
            if (fade <= 0f)
            {
                Destroy(gameObject);
            }
            return;
        }

        if (game == null || game.State != WukongRhythmGame.BattleState.Playing)
        {
            return;
        }

        float beatsRemaining = (targetSongTime - game.SongTime) * game.CurrentBpm / 60f;
        float progress = Mathf.Clamp01(1f - beatsRemaining / warningBeats);
        float visible = Mathf.Lerp(0.14f, 1f, Mathf.SmoothStep(0f, 1f, progress));
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);
        transform.localScale = Vector3.one * Mathf.Lerp(1.32f, 1f, progress);
        SetColor(new Color(0.36f, 0.92f, 1f, visible * Mathf.Lerp(0.24f, 0.68f, pulse)));
    }

    public void CompleteHit(bool perfect)
    {
        resolved = true;
        resolveStarted = Time.unscaledTime;
        resolveColor = perfect ? new Color(0.42f, 1f, 0.95f) : new Color(1f, 0.92f, 0.58f);
    }

    public void CompleteMiss()
    {
        resolved = true;
        resolveStarted = Time.unscaledTime;
        resolveColor = new Color(1f, 0.36f, 0.48f);
    }

    public void Cancel()
    {
        Destroy(gameObject);
    }

    private LineRenderer CreateLine(string lineName, float width)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.widthMultiplier = width;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            line.sharedMaterial = new Material(shader);
        }
        return line;
    }

    private static void SetCircle(LineRenderer line, float radius)
    {
        if (line == null)
        {
            return;
        }

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i / (float)SegmentCount * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private void SetColor(Color color)
    {
        if (outerRing != null)
        {
            outerRing.startColor = color;
            outerRing.endColor = color;
        }
        if (innerRing != null)
        {
            Color inner = color;
            inner.a *= 0.7f;
            innerRing.startColor = inner;
            innerRing.endColor = inner;
        }
    }
}
