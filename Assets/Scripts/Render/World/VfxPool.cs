// @tags: vfx, pool, object-pool, particle, effect, reuse, performance, lifecycle
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 프리팹 단위 VFX 풀. Instantiate/Destroy 왕복을 없앤다.
///
///   전:  var fx = Instantiate(prefab, pos, rot);  Destroy(fx, life);
///   후:  VfxPool.Play(prefab, pos, rot, life);
///
/// 수명은 이 컴포넌트의 Update가 일괄 관리한다. 재생마다 코루틴이나 델리게이트를
/// 만들지 않으므로 호출당 힙 할당이 0이다 — 폭발처럼 한 프레임에 수십 개가 몰릴 때
/// 차이가 난다.
///
/// 풀 루트는 DontDestroyOnLoad라 씬 전환에도 살아남고, 이전 씬에서 재생 중이던
/// 인스턴스는 sceneLoaded에서 일괄 반납한다.
///
/// SOLID — SRP: VFX 인스턴스의 수명과 재사용만 담당. 어떤 이펙트인지는 모른다.
/// </summary>
public class VfxPool : MonoBehaviour
{
    /// <summary>프리팹당 보관 상한. 긴 플레이에서 풀이 무한정 커지는 것을 막는다(ChunkPool과 같은 정책).</summary>
    private const int MaxPerPrefab = 32;

    /// <summary>재사용 인스턴스 1개. 컴포넌트 조회는 생성 시 한 번만 하고 캐시한다.</summary>
    private sealed class Entry
    {
        public GameObject Go;
        public Transform Tr;
        public Vector3 BaseScale;
        public ParticleSystem[] Particles;
        public TrailRenderer[] Trails;
        public Animator[] Animators;
    }

    private struct Active
    {
        public Entry Inst;
        public GameObject Prefab;
        public float ReleaseAt;
    }

    private readonly Dictionary<GameObject, Stack<Entry>> _free = new Dictionary<GameObject, Stack<Entry>>();
    private readonly List<Active> _active = new List<Active>(64);

    private static VfxPool _instance;
    private static bool _quitting;

    // 도메인 리로드를 끈 채로 플레이를 반복하면 static이 이전 세션 값을 물고 있다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }

    private static VfxPool Instance
    {
        get
        {
            if (_instance != null || _quitting) return _instance;

            var go = new GameObject("[VfxPool]");
            _instance = go.AddComponent<VfxPool>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    // ─── 공개 API ─────────────────────────────────────────────────

    /// <summary>
    /// 프리팹을 풀에서 꺼내 재생하고 life초 뒤 자동 반납한다.
    ///
    /// 반환값은 호출측이 스케일·머티리얼 등을 그 자리에서 조정하라고 열어 둔 것이다.
    /// 필드에 보관하지 말 것 — 반납된 뒤 다른 재생에 재사용된다.
    /// </summary>
    public static GameObject Play(GameObject prefab, Vector3 position, Quaternion rotation, float life)
    {
        if (prefab == null) return null;

        var pool = Instance;
        return pool != null ? pool.PlayInternal(prefab, position, rotation, life) : null;
    }

    /// <summary>회전이 필요 없는 이펙트용 오버로드.</summary>
    public static GameObject Play(GameObject prefab, Vector3 position, float life)
        => Play(prefab, position, Quaternion.identity, life);

    /// <summary>로딩 중에 미리 만들어 둔다. 첫 재생의 Instantiate 히칭을 없앤다.</summary>
    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;

        var pool = Instance;
        if (pool == null) return;

        var stack = pool.GetStack(prefab);
        for (int i = 0; i < count && stack.Count < MaxPerPrefab; i++)
        {
            var inst = pool.Create(prefab);
            inst.Go.SetActive(false);
            stack.Push(inst);
        }
    }

    // ─── 내부 ────────────────────────────────────────────────────

    private void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnApplicationQuit() => _quitting = true;

    private void Update()
    {
        float now = Time.time;

        // 뒤에서부터 훑고 swap-back으로 지운다 — 재생 순서는 의미가 없으므로 O(1) 제거.
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (now < _active[i].ReleaseAt) continue;

            Release(_active[i]);
            _active[i] = _active[_active.Count - 1];
            _active.RemoveAt(_active.Count - 1);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 풀 루트가 DontDestroyOnLoad라서, 놔두면 이전 씬의 이펙트가 새 씬에 떠 있게 된다.
        for (int i = 0; i < _active.Count; i++) Release(_active[i]);
        _active.Clear();
    }

    private GameObject PlayInternal(GameObject prefab, Vector3 position, Quaternion rotation, float life)
    {
        var stack = GetStack(prefab);

        Entry inst = null;
        while (stack.Count > 0)
        {
            inst = stack.Pop();
            if (inst.Go != null) break;   // 외부에서 파괴된 잔재는 버린다
            inst = null;
        }
        if (inst == null) inst = Create(prefab);

        inst.Tr.SetPositionAndRotation(position, rotation);
        inst.Tr.localScale = inst.BaseScale;   // 호출측이 스케일을 만졌을 수 있다
        inst.Go.SetActive(true);

        // 재사용 초기화 — 이전 재생의 잔상이 남으면 이펙트가 안 보이거나 겹쳐 보인다.
        for (int i = 0; i < inst.Trails.Length; i++)
            inst.Trails[i].Clear();

        for (int i = 0; i < inst.Animators.Length; i++)
        {
            inst.Animators[i].Rebind();
            inst.Animators[i].Update(0f);      // 첫 프레임을 즉시 적용(한 프레임 이전 포즈 방지)
        }

        for (int i = 0; i < inst.Particles.Length; i++)
        {
            inst.Particles[i].Clear(true);
            inst.Particles[i].Play(true);
        }

        _active.Add(new Active { Inst = inst, Prefab = prefab, ReleaseAt = Time.time + life });
        return inst.Go;
    }

    private void Release(in Active a)
    {
        var inst = a.Inst;
        if (inst.Go == null) return;

        for (int i = 0; i < inst.Particles.Length; i++)
            inst.Particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        inst.Go.SetActive(false);

        var stack = GetStack(a.Prefab);
        if (stack.Count >= MaxPerPrefab) Destroy(inst.Go);
        else stack.Push(inst);
    }

    private Stack<Entry> GetStack(GameObject prefab)
    {
        if (!_free.TryGetValue(prefab, out var stack))
            _free[prefab] = stack = new Stack<Entry>();
        return stack;
    }

    private Entry Create(GameObject prefab)
    {
        var go = Instantiate(prefab, transform);   // 항상 풀 루트의 자식으로 둔다

        return new Entry
        {
            Go         = go,
            Tr         = go.transform,
            BaseScale  = prefab.transform.localScale,
            Particles  = go.GetComponentsInChildren<ParticleSystem>(true),
            Trails     = go.GetComponentsInChildren<TrailRenderer>(true),
            Animators  = go.GetComponentsInChildren<Animator>(true),
        };
    }
}
