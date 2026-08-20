using UnityEngine;

public sealed class WukongBeatRock : MonoBehaviour
{
    private WukongRhythmGame game;
    private Vector3 startPoint;
    private Vector3 targetPoint;
    private Vector3 controlPoint;
    private float spawnSongTime;
    private float targetSongTime;
    private int lane;
    private bool spellRock;
    private bool resolved;
    private Renderer cachedRenderer;
    private Light coreLight;
    private WukongHitWarningRing warningRing;

    public bool IsResolved => resolved;
    public float TargetSongTime => targetSongTime;

    public void Initialize(
        WukongRhythmGame owner,
        Vector3 start,
        Vector3 target,
        float spawnTime,
        float targetTime,
        int targetLane,
        bool isSpellRock,
        float warningBeats)
    {
        game = owner;
        startPoint = start;
        targetPoint = target;
        spawnSongTime = spawnTime;
        targetSongTime = targetTime;
        lane = targetLane;
        spellRock = isSpellRock;

        Vector3 midpoint = Vector3.Lerp(startPoint, targetPoint, 0.5f);
        controlPoint = midpoint + Vector3.up * (spellRock ? 5.5f : 3.4f) + game.playerCamera.transform.right * lane * 0.35f;
        transform.position = startPoint;
        transform.localScale = Vector3.one * (spellRock ? 0.54f : Random.Range(0.39f, 0.47f));
        transform.rotation = Random.rotation;

        cachedRenderer = GetComponent<Renderer>();
        if (cachedRenderer != null)
        {
            cachedRenderer.sharedMaterial = game.rockMaterial;
        }

        coreLight = gameObject.GetComponent<Light>();
        if (coreLight == null)
        {
            coreLight = gameObject.AddComponent<Light>();
        }
        coreLight.type = LightType.Point;
        coreLight.color = spellRock ? new Color(1f, 0.12f, 0.01f) : new Color(1f, 0.34f, 0.04f);
        coreLight.intensity = spellRock ? 720f : 360f;
        coreLight.range = spellRock ? 4.8f : 3.4f;
        warningRing = WukongHitWarningRing.Create(game, targetPoint, targetSongTime, warningBeats, spellRock);
    }

    private void Update()
    {
        if (resolved || game == null || game.State != WukongRhythmGame.BattleState.Playing)
        {
            return;
        }

        float duration = Mathf.Max(0.1f, targetSongTime - spawnSongTime);
        float t = Mathf.Clamp01((game.SongTime - spawnSongTime) / duration);
        float eased = Mathf.SmoothStep(0f, 1f, t);
        transform.position = QuadraticBezier(startPoint, controlPoint, targetPoint, eased);
        transform.Rotate(new Vector3(87f, 123f, 56f) * Time.deltaTime, Space.Self);

        float pulse = 0.82f + Mathf.Sin(Time.time * (spellRock ? 13f : 9f)) * 0.18f;
        coreLight.intensity = (spellRock ? 720f : 360f) * pulse;

        float distanceToTarget = Vector3.Distance(transform.position, targetPoint);
        if (game.staff != null)
        {
            if (game.staff.TryStrike(transform.position, spellRock ? 0.72f : 0.58f))
            {
                Shatter(Mathf.Abs(game.SongTime - targetSongTime));
                return;
            }

        }

        if (game.SongTime > targetSongTime + game.hitWindow)
        {
            Miss();
        }
    }

    private void Shatter(float timingError)
    {
        resolved = true;
        if (cachedRenderer != null)
        {
            cachedRenderer.enabled = false;
        }
        Collider hitCollider = GetComponent<Collider>();
        if (hitCollider != null)
        {
            hitCollider.enabled = false;
        }

        if (warningRing != null)
        {
            warningRing.CompleteHit(timingError <= game.hitWindow * 0.46f);
        }
        game.ResolveHit(this, timingError);
        game.SpawnRockExplosion(transform.position, spellRock);
        Destroy(gameObject);
    }

    private void Miss()
    {
        resolved = true;
        if (warningRing != null)
        {
            warningRing.CompleteMiss();
        }
        game.ResolveMiss(this);
        game.SpawnRockExplosion(targetPoint + game.playerCamera.transform.forward * 0.4f, false);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!resolved && warningRing != null)
        {
            warningRing.Cancel();
        }
    }

    private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * a + 2f * inverse * t * b + t * t * c;
    }
}
