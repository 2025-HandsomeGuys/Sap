using System;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 자석(패시브·상시): 장착 중이면 매 프레임 반경 내 "파낸 광물"을 플레이어로 끌어와 자동 수거.
    // 레벨업(F7)으로 흡인 반경이 커진다.
    //  · 흡인 딜레이: 범위 진입 직후 바로 빨려오지 않고 pullDelay 동안 대기(예고) 후 당김 시작.
    //  · 하이라이트: 흡인 대상 광물에 약한 아웃라인을 켜 "끌려오는 중"임을 알린다(MineralPickupGlow 재사용).
    //  · 누적 가속: 당김이 시작된 뒤 오래 따라올수록 흡인 속도가 점점 빨라진다. 범위를 벗어나면 리셋.
    //  · 자력 오라: 흡인 대상이 하나라도 있으면 플레이어 주위에 옅은 링을 켜고, 다 먹어 대상이 없으면 끈다.
    [Serializable]
    public class MagnetRelic : RelicBehaviour
    {
        [SerializeField] private float[] radiusPerLevel = { 4f, 5f, 6f };
        [SerializeField] private float   basePullSpeed = 2f;    // 따라오기 시작할 때 속도(units/sec)
        [SerializeField] private float   pullAccel     = 6f;    // 따라온 1초당 속도 증가량
        [SerializeField] private float   maxPullSpeed  = 20f;   // 속도 상한
        [SerializeField] private float   collectRadius = 0.1f;  // 플레이어 몸에 닿을 만큼 가까워야 수거
        [SerializeField] private LayerMask itemMask = ~0;       // 인스펙터에서 "Mineral" 레이어로 좁히면 성능↑

        [Header("흡인 딜레이")]
        [SerializeField] private float pullDelay = 0.35f;       // 범위 진입 후 흡인 시작까지 지연(초)

        [Header("자력 오라(살짝)")]
        [SerializeField] private float auraAlpha = 0.18f;       // 오라 최대 알파(옅게)
        [SerializeField] private float auraWidth = 0.05f;       // 오라 선 두께
        [SerializeField] private float auraFadeSpeed = 5f;      // 켜짐/꺼짐 페이드 속도
        [SerializeField] private Color auraColor = new Color(0.4f, 0.9f, 1f, 1f);

        // 파낸(월드에 떨어진) 광물만 흡인. 땅속 매설 광물(Untagged)은 제외.
        private const string TargetTag = "MineralDug";

        private readonly Collider2D[] _buffer = new Collider2D[32];
        // OverlapCircleNonAlloc 대체. useTriggers는 구 API와 동일하게 전역 설정을 따라간다
        // (ContactFilter2D 기본값은 false라 그냥 두면 트리거 콜라이더 광물이 안 잡힌다).
        private ContactFilter2D _filter;

        // 광물별 추적 상태(딜레이·가속·하이라이트). key = collider instanceID.
        private class Target
        {
            public float age;                 // 범위 진입 후 경과 시간
            public MineralPickupGlow glow;     // 하이라이트 대상(없을 수 있음)
        }
        private Dictionary<int, Target> _targets;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _toRemove = new List<int>();

        // 가방 가득참 판정용(런타임 캐시).
        private MineralInventory _mineralInv;

        // 자력 오라(플레이어 주위 옅은 링). 코드 생성, 프리팹 불필요.
        private const int   AuraSegments = 48;
        private GameObject   _aura;
        private LineRenderer _auraLr;
        private Material     _auraMat;
        private float        _auraCurAlpha;    // 현재 페이드 알파

        private float Lv(float[] arr) => arr[Mathf.Clamp(level - 1, 0, arr.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _targets = new Dictionary<int, Target>();
        }

        public override void OnUpdate()
        {
            if (ctx?.player == null) return;
            if (_targets == null) _targets = new Dictionary<int, Target>();

            Vector2 center = ctx.player.position;
            float r = Lv(radiusPerLevel);
            float dt = Time.deltaTime;

            // 가방이 가득 차면 자력 전체 OFF: 추적·하이라이트·오라 모두 끈다.
            if (InventoryFull())
            {
                ClearAllTargets();
                UpdateAura(center, r, false, dt);
                return;
            }

            _seen.Clear();

            // itemMask는 인스펙터에서 바뀔 수 있어 매 프레임 필터에 반영(구조체 필드 대입이라 비용 없음)
            _filter.useTriggers = Physics2D.queriesHitTriggers;
            _filter.SetLayerMask(itemMask);
            int count = Physics2D.OverlapCircle(center, r, _filter, _buffer);
            for (int i = 0; i < count; i++)
            {
                var col = _buffer[i];
                if (col == null) continue;
                if (!col.CompareTag(TargetTag)) continue; // 파낸 광물만
                if (col.transform == ctx.player) continue;

                int id = col.GetInstanceID();
                var rb = col.attachedRigidbody;
                Vector2 mpos = rb != null ? rb.position : (Vector2)col.transform.position;

                // 플레이어에 충분히 가까우면 기존 픽업 파이프라인으로 자동 수거
                if (Vector2.Distance(mpos, center) <= collectRadius)
                {
                    if (col.TryGetComponent<PickupableItem>(out var pickup))
                        pickup.Interact(ctx.player.gameObject);
                    DropTarget(id);
                    continue;
                }

                // 추적 상태 확보(신규 진입이면 생성 + 하이라이트 ON)
                if (!_targets.TryGetValue(id, out var tgt))
                {
                    tgt = new Target();
                    col.TryGetComponent(out tgt.glow);
                    tgt.glow?.SetMagnetHighlighted(true);
                    _targets[id] = tgt;
                }
                tgt.age += dt;
                _seen.Add(id);

                // 딜레이 구간: 아직 당기지 않고 대기(하이라이트로 예고만).
                if (tgt.age < pullDelay) continue;

                // 당김 시작 후 경과에 비례해 가속(상한까지).
                float followT = tgt.age - pullDelay;
                float speed = Mathf.Min(basePullSpeed + pullAccel * followT, maxPullSpeed);
                float maxDelta = speed * dt;

                // 힘이 아니라 위치로 당김 → Kinematic(안착)·Dynamic(낙하) 모두 이동.
                Vector2 next = Vector2.MoveTowards(mpos, center, maxDelta);
                if (rb != null) { rb.linearVelocity = Vector2.zero; rb.position = next; }
                else col.transform.position = next;
            }

            // 이번 프레임에 범위 밖이 된 광물은 하이라이트 끄고 제거 → 재진입 시 처음부터.
            if (_targets.Count > _seen.Count)
            {
                _toRemove.Clear();
                foreach (var kv in _targets)
                    if (!_seen.Contains(kv.Key)) _toRemove.Add(kv.Key);
                for (int i = 0; i < _toRemove.Count; i++)
                    DropTarget(_toRemove[i]);
            }

            // 자력 오라: 흡인 대상이 하나라도 있으면 켜고, 다 먹으면 끈다.
            UpdateAura(center, r, _seen.Count > 0, dt);
        }

        public override void OnUnequip()
        {
            if (_targets != null)
            {
                foreach (var kv in _targets)
                    kv.Value.glow?.SetMagnetHighlighted(false);
                _targets.Clear();
            }
            if (_aura != null) UnityEngine.Object.Destroy(_aura);
            if (_auraMat != null) UnityEngine.Object.Destroy(_auraMat);
            _aura = null; _auraLr = null; _auraMat = null; _auraCurAlpha = 0f;
        }

        // 가방이 가득 찼는지(더 이상 광물을 받을 수 없는지).
        // InventoryUI가 없는 씬(코드 생성 오버레이만 쓰는 경우)에서도 찾아야 한다.
        // 못 찾으면 '가득 차지 않음'으로 취급돼 꽉 찬 가방에도 계속 끌어당기므로 폴백이 필요하다.
        private bool InventoryFull()
        {
            if (_mineralInv == null)
            {
                var ui = InventoryUI.Instance;
                _mineralInv = (ui != null && ui.mineralInventory != null)
                    ? ui.mineralInventory
                    : UnityEngine.Object.FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
            }
            return _mineralInv != null && _mineralInv.IsFull;
        }

        // 모든 추적 대상의 하이라이트를 끄고 비운다.
        private void ClearAllTargets()
        {
            if (_targets == null || _targets.Count == 0) return;
            foreach (var kv in _targets)
                kv.Value.glow?.SetMagnetHighlighted(false);
            _targets.Clear();
            _seen.Clear();
        }

        // 추적 해제 + 하이라이트 끄기.
        private void DropTarget(int id)
        {
            if (_targets.TryGetValue(id, out var tgt))
            {
                tgt.glow?.SetMagnetHighlighted(false);
                _targets.Remove(id);
            }
        }

        // 플레이어 주위 옅은 링을 대상 유무에 따라 페이드 in/out.
        private void UpdateAura(Vector2 center, float radius, bool active, float dt)
        {
            float target = active ? auraAlpha : 0f;
            _auraCurAlpha = Mathf.MoveTowards(_auraCurAlpha, target, auraFadeSpeed * auraAlpha * dt);

            if (_auraCurAlpha <= 0.001f)
            {
                if (_aura != null && _aura.activeSelf) _aura.SetActive(false);
                return;
            }

            EnsureAura();
            if (_aura == null) return;

            if (!_aura.activeSelf) _aura.SetActive(true);
            _aura.transform.position = center;         // 부모 미부착(플레이어 스케일/반전 상속 회피)
            _aura.transform.localScale = Vector3.one * radius;

            var c = auraColor; c.a = _auraCurAlpha;
            _auraLr.startColor = c; _auraLr.endColor = c;
        }

        private void EnsureAura()
        {
            if (_aura != null) return;

            _aura = new GameObject("RelicMagnetAura");
            _auraLr = _aura.AddComponent<LineRenderer>();
            _auraLr.useWorldSpace = false;
            _auraLr.loop = true;
            _auraLr.positionCount = AuraSegments;
            _auraLr.widthMultiplier = auraWidth;
            _auraLr.numCapVertices = 2;
            _auraMat = new Material(Shader.Find("Sprites/Default"));
            _auraLr.material = _auraMat;
            _auraLr.sortingOrder = 90;

            var c = auraColor; c.a = 0f;
            _auraLr.startColor = c; _auraLr.endColor = c;

            for (int i = 0; i < AuraSegments; i++)
            {
                float ang = (i / (float)AuraSegments) * Mathf.PI * 2f;
                _auraLr.SetPosition(i, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f)); // 단위원
            }

            _aura.SetActive(false);
        }
    }
}
