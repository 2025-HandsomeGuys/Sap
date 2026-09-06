using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 일회용 포탈(액티브·토글): 1차 발동 시 현재 위치에 포탈 설치(Active 진입, 시간 무제한),
    // 2차 발동 시 그 포탈로 귀환 + 포탈 소멸 → 쿨다운 시작. 레벨업은 쿨다운 감소만.
    //
    //  - GetDuration()=무한 + IsToggle=true → 종료 진입점은 토글 재입력(CancelToCooldown→OnActiveEnd)과
    //    OnUnequip뿐. 자연 만료(activeEnded) 경로는 실질적으로 안 탄다.
    //  - 귀환 텔레포트는 ElevatorManager.TeleportPlayer와 동일하게 player.position 직접 세팅 —
    //    InfinityMapManager가 주변 청크를 자동 스트리밍하므로 별도 로딩 처리 불필요.
    //  - 던전 안(DungeonOverlayController.IsInDungeon)에서는 설치·귀환 모두 발동 불가
    //    (던전 탈출 악용/오버레이 상태 파손 방지). CanActivate 게이트라 발동 소모 없음.
    //  - 포탈은 세이브 비저장(지하 세션 한정) — 씬 전환 시 액티브 상태와 함께 자연 리셋.
    //  - 비주얼은 전부 코드 생성(타원 링 + 회전 스월, 프리팹·에셋 불필요).
    [Serializable]
    public class OneWayPortalRelic : RelicBehaviour
    {
        [SerializeField] private float[] cooldownPerLevel = { 540f, 420f, 300f }; // 9/7/5분

        [Header("포탈 비주얼")]
        [SerializeField] private Color portalColor   = new Color(0.55f, 0.35f, 1f, 0.9f);  // 보라
        [SerializeField] private Color swirlColor    = new Color(0.8f, 0.65f, 1f, 0.7f);   // 밝은 보라
        [SerializeField] private float portalRadiusX = 0.45f;  // 세로 긴 타원
        [SerializeField] private float portalRadiusY = 0.8f;
        [SerializeField] private float spawnPopTime  = 0.25f;  // 등장 스케일 팝
        [SerializeField] private float pulseSpeed    = 2.2f;
        [SerializeField] private float swirlSpinSpeed = 90f;   // deg/sec

        [Header("귀환 플래시")]
        [SerializeField] private float flashDuration = 0.3f;
        [SerializeField] private float flashRadius   = 1.2f;

        private GameObject   _portalGo;    // 설치된 포탈 비주얼 루트
        private LineRenderer _ringLr;
        private LineRenderer _swirlLr;
        private Material     _portalMat;
        private Vector2      _portalPos;   // 귀환 목표 위치
        private Rigidbody2D  _rb;          // 낙하속도 이월 방지용(런타임 캐시)

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetDuration() => float.MaxValue;  // 귀환 전까지 무제한 유지
        public override float GetCooldown() => Lv(cooldownPerLevel);
        public override bool  IsToggle      => true;            // 재입력 = 귀환

        public override bool CanActivate() => !DungeonOverlayController.IsInDungeon;

        // ── 1차 발동: 포탈 설치 ──
        public override void OnActivate()
        {
            if (ctx?.player == null) return;
            _portalPos = ctx.player.position;
            CreatePortalVisual(_portalPos);
        }

        // 대기 중 연출: 등장 팝 + 느린 펄스 + 스월 회전
        public override void OnActiveUpdate(float elapsed)
        {
            if (_portalGo == null) return;

            float pop = spawnPopTime > 0f ? Mathf.Clamp01(elapsed / spawnPopTime) : 1f;
            _portalGo.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, pop);

            float pulse = 0.85f + 0.15f * Mathf.Sin(elapsed * pulseSpeed);
            if (_ringLr != null)
            {
                Color c = portalColor; c.a = portalColor.a * pulse * pop;
                _ringLr.startColor = c; _ringLr.endColor = c;
            }
            if (_swirlLr != null)
                _swirlLr.transform.localRotation = Quaternion.Euler(0f, 0f, elapsed * swirlSpinSpeed);
        }

        // ── 2차 발동(토글 조기종료): 귀환 + 포탈 소멸. 쿨다운은 상태기계가 자동 시작 ──
        public override void OnActiveEnd()
        {
            if (_portalGo == null) return; // 설치 없이 종료(방어)

            if (ctx?.player != null)
            {
                Vector2 from = ctx.player.position;

                // 텔레포트 (엘리베이터 검증 경로) + 낙하속도 이월 방지
                ctx.player.position = _portalPos;
                if (_rb == null) _rb = ctx.player.GetComponentInParent<Rigidbody2D>();
                if (_rb != null) _rb.linearVelocity = Vector2.zero;

                // 텔레포트가 성사된 뒤에만 재생한다(위의 _portalGo == null 방어 분기로
                // 빠지면 소리만 나고 이동은 안 하는 상황이 된다)
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFXAt(SfxKeys.RelicPortalReturn, _portalPos);

                // 출발·도착 양쪽 플래시
                if (ctx.runner != null)
                {
                    ctx.runner.StartCoroutine(FlashRing(from));
                    ctx.runner.StartCoroutine(FlashRing(_portalPos));
                }
            }

            DestroyPortalVisual();
        }

        // 해제 시 포탈만 정리(귀환 없음)
        public override void OnUnequip() => DestroyPortalVisual();

        // ── 비주얼 ──

        private void CreatePortalVisual(Vector2 pos)
        {
            DestroyPortalVisual();

            _portalGo = new GameObject("RelicOneWayPortal");
            _portalGo.transform.position = new Vector3(pos.x, pos.y, 0f);
            _portalGo.transform.localScale = Vector3.zero; // 등장 팝으로 키움
            _portalMat = new Material(Shader.Find("Sprites/Default"));

            // 외곽 타원 링 (로컬 좌표 — 루트 스케일/회전 연출을 위해)
            _ringLr = CreateLoop(_portalGo.transform, "Ring", 0.12f, portalColor, 116);
            const int seg = 48;
            _ringLr.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                _ringLr.SetPosition(i, new Vector3(Mathf.Cos(a) * portalRadiusX, Mathf.Sin(a) * portalRadiusY, 0f));
            }

            // 내부 스월(나선) — 자식 회전으로 빙글빙글
            var swirlGo = new GameObject("Swirl");
            swirlGo.transform.SetParent(_portalGo.transform, false);
            _swirlLr = CreateLoop(swirlGo.transform, null, 0.07f, swirlColor, 115);
            _swirlLr.loop = false;
            const int spiralSeg = 36;
            _swirlLr.positionCount = spiralSeg;
            for (int i = 0; i < spiralSeg; i++)
            {
                float k = i / (float)(spiralSeg - 1);          // 0..1
                float a = k * Mathf.PI * 4f;                   // 2바퀴
                float r = Mathf.Lerp(0.06f, 0.75f, k);         // 중심→바깥
                _swirlLr.SetPosition(i, new Vector3(Mathf.Cos(a) * r * portalRadiusX,
                                                    Mathf.Sin(a) * r * portalRadiusY, 0f));
            }
        }

        private LineRenderer CreateLoop(Transform parent, string childName, float width, Color color, int order)
        {
            Transform t = parent;
            if (childName != null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            var lr = t.gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            lr.material = _portalMat;
            lr.sortingOrder = order;
            lr.startColor = color; lr.endColor = color;
            return lr;
        }

        private void DestroyPortalVisual()
        {
            if (_portalGo != null) UnityEngine.Object.Destroy(_portalGo);
            if (_portalMat != null) UnityEngine.Object.Destroy(_portalMat);
            _portalGo = null; _ringLr = null; _swirlLr = null; _portalMat = null;
        }

        // 귀환 플래시 링 (Steroid ShockwaveRing 패턴)
        private IEnumerator FlashRing(Vector2 center)
        {
            var go = new GameObject("RelicPortalFlash");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 40;
            lr.positionCount = seg;
            lr.widthMultiplier = 0.12f;
            lr.numCapVertices = 4;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 117;

            float t = 0f;
            while (t < flashDuration)
            {
                float k = t / flashDuration;
                float r = Mathf.Lerp(0.15f, flashRadius, k);
                Color c = portalColor; c.a = portalColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;

                for (int i = 0; i < seg; i++)
                {
                    float a = (i / (float)seg) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }

                t += Time.deltaTime;
                yield return null;
            }

            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(go);
        }
    }
}
