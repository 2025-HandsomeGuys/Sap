// @tags: relic, drop, pickup, interactable, world, exploration, code-generated
using Relic.Data;
using UnityEngine;

namespace Relic.Drop
{
    /// <summary>
    /// 월드 바닥에 떨어진 유물. F키로 주우면 <see cref="RelicManager"/>에 지급된다.
    ///
    /// 프리팹이 없다 — <see cref="Create"/>가 런타임에 조립한다. 유물 28종마다 프리팹을 만들면
    /// 스프라이트만 다른 껍데기가 28개 생기고, 그림은 <c>RelicSO.icon</c>에 이미 있으므로 중복이다.
    ///
    /// 광물과 달리 <c>MineralLifetime</c>(60초 소멸)을 붙이지 않는다. 유물은 종당 1개뿐이라
    /// 놓치면 그 회차에 만회할 방법이 없기 때문이다. 대신 씬 전환·게임 종료로 사라질 수는 있는데,
    /// 그때도 <b>보유 처리는 안 된 상태</b>라 다음 추첨에서 다시 나온다 — 영구 손실은 없다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class WorldRelicPickup : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        private const string PromptKey      = "interact_relic_pickup";
        private const string PromptFallback = "유물 줍기";

        /// <summary>
        /// 줍기 판정 반경(월드 유닛). 스프라이트 크기와 무관하게 일정해야 하므로
        /// 아래에서 <see cref="WorldSize"/> 정규화 스케일을 되돌려 적용한다.
        /// </summary>
        private const float TriggerRadius = 0.45f;

        /// <summary>둥실거리는 폭·주기. 배경 지형과 섞여 안 보이는 걸 막는 최소한의 연출.</summary>
        private const float BobAmplitude = 0.06f;
        private const float BobSpeed     = 2.2f;

        /// <summary>월드에서 차지할 긴 변 길이(유닛). 광물 픽업(약 0.4)보다 약간 크게 둔다.</summary>
        private const float WorldSize = 0.5f;

        [SerializeField] private RelicID relicId = RelicID.None;

        private Vector3 _basePosition;
        private bool    _collected;

        public RelicID RelicId => relicId;

        /// <summary>
        /// 유물 픽업을 월드에 만든다.
        ///
        /// 아이콘이 비어 있으면 <see cref="PlaceholderSprite"/>로 대신한다. 유물 28종 중 20종이
        /// 아직 아이콘 미제작인데, 상점을 닫은 지금 드롭이 유일한 획득 경로라 여기서 생성을 포기하면
        /// <b>그 유물들이 영영 못 얻는 것</b>이 된다. 보이지 않는 픽업보다 임시 도형이 낫다.
        /// </summary>
        public static WorldRelicPickup Create(RelicID id, Vector3 worldPos, int sortingOrder)
        {
            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (so == null)
            {
                Debug.LogWarning($"[WorldRelicPickup] RelicDatabase에 {id} 없음 — 생성 취소");
                return null;
            }

            Sprite sprite = so.icon;
            if (sprite == null)
            {
                sprite = PlaceholderSprite;
                Debug.LogWarning($"[WorldRelicPickup] {id} 아이콘 미제작 — 임시 도형으로 대체");
            }

            var go = new GameObject($"RelicPickup_{id}");
            go.transform.position = worldPos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.sortingOrder = sortingOrder;

            // RelicSO.icon은 UI용 스프라이트라 해상도·PPU가 제각각이다(256px/PPU100이면 2.5유닛짜리
            // 거대한 유물이 바닥에 눕는다). 긴 변을 WorldSize로 맞춰 월드에서 항상 같은 크기로 보이게 한다.
            Vector2 size = sprite.bounds.size;
            float longest = Mathf.Max(size.x, size.y);
            if (longest > 0.0001f)
                go.transform.localScale = Vector3.one * (WorldSize / longest);

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            // 콜라이더는 로컬 좌표라 위 스케일이 그대로 곱해진다 — 나눠서 상쇄한다.
            float scale = go.transform.localScale.x;
            col.radius = scale > 0.0001f ? TriggerRadius / scale : TriggerRadius;

            var pickup = go.AddComponent<WorldRelicPickup>();
            pickup.relicId = id;
            return pickup;
        }

        // ── 아이콘 미제작 유물용 임시 도형 ──────────────────────────────────

        private const int PlaceholderPixels = 32;
        private static Sprite s_placeholder;

        /// <summary>
        /// 유물 색(<c>CodeUI.RelicColor</c>) 마름모. 아이콘이 붙기 전까지의 자리표시자다.
        /// 텍스처를 한 번만 만들어 모든 픽업이 공유한다.
        /// </summary>
        private static Sprite PlaceholderSprite
        {
            get
            {
                if (s_placeholder != null) return s_placeholder;

                var tex = new Texture2D(PlaceholderPixels, PlaceholderPixels, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode   = TextureWrapMode.Clamp,
                    name       = "RelicPickupPlaceholder"
                };

                Color fill    = CodeUI.RelicColor;
                Color outline = Color.Lerp(fill, Color.black, 0.55f);
                const float half = PlaceholderPixels * 0.5f;

                for (int y = 0; y < PlaceholderPixels; y++)
                for (int x = 0; x < PlaceholderPixels; x++)
                {
                    // |dx| + |dy| <= r 이면 마름모 안. 테두리는 안쪽 마름모와의 차집합.
                    float d = Mathf.Abs(x + 0.5f - half) + Mathf.Abs(y + 0.5f - half);
                    Color c = d > half        ? Color.clear
                            : d > half - 2.5f ? outline
                                              : fill;
                    tex.SetPixel(x, y, c);
                }
                tex.Apply();

                s_placeholder = Sprite.Create(tex, new Rect(0, 0, PlaceholderPixels, PlaceholderPixels),
                                              new Vector2(0.5f, 0.5f), pixelsPerUnit: 64f);
                s_placeholder.name = "RelicPickupPlaceholder";
                return s_placeholder;
            }
        }

        private void Start()
        {
            _basePosition = transform.position;
        }

        private void Update()
        {
            if (_collected) return;
            float y = Mathf.Sin(Time.time * BobSpeed) * BobAmplitude;
            transform.position = _basePosition + new Vector3(0f, y, 0f);
        }

        // --- IInteractable / IInteractionPrompt ---

        public string InteractionPrompt => PromptFallback;

        /// <summary>엘리베이터(10)보다 낮고 광물(0)보다 높다 — 광물 더미에 묻혀도 유물이 먼저 잡힌다.</summary>
        public int InteractionPriority => 5;

        public bool CanInteract => !_collected && relicId != RelicID.None;

        public InteractionPromptInfo GetInteractionPrompt()
            => CanInteract ? InteractionPromptInfo.Ok(PromptKey, PromptFallback) : InteractionPromptInfo.None;

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;

            var mgr = RelicManager.EnsureInScene();
            if (mgr == null)
            {
                Debug.LogError("[WorldRelicPickup] RelicManager를 찾을 수 없어 유물을 지급하지 못했다.");
                return;
            }

            _collected = true;
            mgr.GrantAndAutoEquip(relicId);

            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(relicId) : null;
            if (so != null) AcquisitionNotifier.NotifyRelic(so);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFXAt(SfxKeys.MineralPickup, transform.position);

            Debug.Log($"[WorldRelicPickup] 유물 획득: {relicId}");
            Destroy(gameObject);
        }
    }
}
