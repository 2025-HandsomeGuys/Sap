using System;
using UnityEngine;

namespace Relic
{
    // 제트팩(패시브·드릴모드 한정): 드릴을 선택하면 파기가 비활성화되고 호버 비행 모드가 된다.
    // 지면에서 위(점프/W) 입력으로 이륙하면 중력이 꺼진 채 고도를 자동 유지(호버)하며,
    // 방향키로 상하좌우 자유 비행한다. 비행 중 드릴 배터리를 소모하고, 바닥에 내려서면 걷기로 복귀.
    // 레벨↑ = 비행속도↑ + 연료효율↑.
    [Serializable]
    public class JetpackRelic : RelicBehaviour
    {
        [Tooltip("레벨별 상하 비행 속도(무입력 시 0=제자리 호버). <jumpForce×1.2 권장")]
        [SerializeField] private float[] ascendSpeedPerLevel = { 2f, 2.5f, 3f };

        [Tooltip("레벨별 초당 연료(드릴 배터리) 소모(상승 기준). 호버·하강은 절반. 레벨↑ = 효율↑")]
        [SerializeField] private float[] drainPerLevel = { 1.4f, 1.1f, 0.85f };

        [Header("호버 흔들림")]
        [Tooltip("정지 호버 중 상하로 흔들리는 속도 진폭(0이면 비활성). 고도는 유지된다")]
        [SerializeField] private float hoverBobAmplitude = 0.6f;

        [Tooltip("호버 흔들림 주파수(Hz)")]
        [SerializeField] private float hoverBobFrequency = 1.1f;

        [Header("연료 고갈 털털거림")]
        [Tooltip("연료 비율이 이 값 이하로 떨어지면 추력이 끊기기 시작한다(0이면 비활성)")]
        [SerializeField] private float sputterStartRatio = 0.25f;

        [Tooltip("추력이 끊긴 순간 가라앉는 속도(고갈에 가까울수록 강해짐)")]
        [SerializeField] private float sputterSinkSpeed = 3f;

        private bool _suppressing; // 우리가 드릴 suppress를 켰는지 추적
        private bool _flying;      // 호버 비행 모드 진입 여부

        private float _misfireUntil;  // 이 시각까지 추력 끊김
        private float _nextMisfireAt; // 다음 끊김 예정 시각

        private ParticleSystem _exhaust;          // 하강 추진 연기(코드 생성, 프리팹 불필요)
        private ParticleSystem.EmissionModule _exhaustEmission;

        private float Ascend() => ascendSpeedPerLevel[Mathf.Clamp(level - 1, 0, ascendSpeedPerLevel.Length - 1)];
        private float Drain()  => drainPerLevel[Mathf.Clamp(level - 1, 0, drainPerLevel.Length - 1)];

        public override void OnUpdate()
        {
            var tools  = ctx?.tools;
            var mining = ctx?.mining;
            var pc     = ctx?.controller;
            if (tools == null || mining == null || pc == null) { Cleanup(); return; }

            // UI/로딩 열림 중이거나 드릴 모드가 아니면 정지 상태로 되돌린다.
            bool uiBlocked = UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None;
            if (uiBlocked || !tools.IsDrillMode) { Cleanup(); return; }

            // 드릴 모드: 드릴을 완전 정지시킨다(파기 대신 비행).
            if (!_suppressing) { mining.SuppressMining = true; _suppressing = true; }

            // 입력: 위=점프키 또는 W/↑, 아래=S/↓.
            bool hasFuel  = mining.GetDrillBatteryRatio() > 0f;
            float vAxis   = Input.GetAxisRaw("Vertical");
            bool up       = Input.GetButton("Jump") || vAxis > 0.1f;
            bool down     = vAxis < -0.1f;
            bool grounded = pc.IsGrounded;

            // 이륙: 지면에서 위 입력 + 연료 있고 벽타기 아닐 때 비행 진입.
            if (!_flying && grounded && up && hasFuel && !pc.IsWallClimbing)
                _flying = true;

            // 비행 종료 조건: 연료 소진 또는 벽타기 전환.
            if (_flying && (!hasFuel || pc.IsWallClimbing))
                _flying = false;

            if (_flying)
            {
                float fly = Ascend();
                float vy  = up ? fly : (down ? -fly : 0f); // 무입력 = 그 자리 고도 유지(호버)

                // 정지 호버 중엔 미세하게 위아래로 흔들린다(추진기 아이들 느낌).
                if (!up && !down) vy += HoverBob();

                // 연료 바닥 근처: 추력이 불규칙하게 끊기며 털털거린다(0=정상, 1=거의 소진).
                float sputter   = SputterAmount(mining.GetDrillBatteryRatio());
                bool  misfiring = sputter > 0f && IsMisfiring(sputter);
                if (misfiring)
                    vy = -sputterSinkSpeed * sputter; // 추력 상실 → 뚝 가라앉음

                pc.SetJetpackThrust(true, vy);

                // 연료: 상승은 전액, 호버·하강은 절반 소모.
                mining.RefillBattery(-Drain() * (up ? 1f : 0.5f) * Time.deltaTime);

                // 착지: 지면에 닿았고 상승 의사 없으면 비행 종료(걷기 복귀).
                if (grounded && !up)
                {
                    _flying = false;
                    pc.SetJetpackThrust(false, 0f);
                }

                // 분사는 '의도' 기준. vy로 판정하면 호버 흔들림 때문에 연기가 깜빡거린다.
                // 하강 입력 중이거나 추력이 끊긴 순간엔 분사가 멎는다(털털거림이 눈에 보임).
                SetExhaust(_flying && !down && !misfiring);
            }
            else
            {
                pc.SetJetpackThrust(false, 0f);
                SetExhaust(false);
                _misfireUntil = _nextMisfireAt = 0f;
            }
        }

        // 정지 호버 중 상하 흔들림(속도에 더하는 값).
        // 평균 0인 사인파라 적분값=고도가 제자리로 수렴 → 고도 유지가 깨지지 않는다.
        // 주파수가 다른 두 파를 섞어 기계적인 왕복처럼 안 보이게 한다.
        private float HoverBob()
        {
            if (hoverBobAmplitude <= 0f) return 0f;
            float t = Time.time * hoverBobFrequency * Mathf.PI * 2f;
            return hoverBobAmplitude * (Mathf.Sin(t) * 0.7f + Mathf.Sin(t * 1.7f) * 0.3f);
        }

        // 연료 비율 → 털털거림 강도(0=정상, 1=거의 소진). 임계치 이상이면 0.
        private float SputterAmount(float fuelRatio)
        {
            if (sputterStartRatio <= 0f) return 0f;
            return Mathf.Clamp01(1f - fuelRatio / sputterStartRatio);
        }

        // 불규칙 추력 끊김 타이머. 고갈에 가까울수록 끊김이 길어지고 간격이 짧아진다.
        private bool IsMisfiring(float sputter)
        {
            float now = Time.time;
            if (now >= _nextMisfireAt)
            {
                // 끊김 0.05~0.16초, 간격 0.9~0.12초(강도에 따라). ±30% 지터로 규칙성을 깬다.
                float cut = Mathf.Lerp(0.05f, 0.16f, sputter);
                float gap = Mathf.Lerp(0.9f, 0.12f, sputter) * UnityEngine.Random.Range(0.7f, 1.3f);
                _misfireUntil  = now + cut;
                _nextMisfireAt = _misfireUntil + gap;

                if (CameraShakeManager.Instance != null)
                    CameraShakeManager.Instance.Shake(cut, 0.03f * sputter);
            }
            return now < _misfireUntil;
        }

        public override void OnUnequip()
        {
            Cleanup();
            if (_exhaust != null) UnityEngine.Object.Destroy(_exhaust.gameObject);
            _exhaust = null;
        }

        // 드릴 복귀 + 추진 off. 드릴모드 이탈/UI/해제 시 호출.
        private void Cleanup()
        {
            var mining = ctx?.mining;
            if (_suppressing && mining != null) mining.SuppressMining = false;
            _suppressing = false;
            _flying = false;
            ctx?.controller?.SetJetpackThrust(false, 0f);
            SetExhaust(false);
        }

        // 하강 추진 연기 on/off. 최초 요청 시 코드로 생성한다.
        private void SetExhaust(bool on)
        {
            // 파티클 생성 실패로 early return 하기 전에 소리를 처리한다.
            // Cleanup()이 SetExhaust(false)를 부르므로 해제·이탈 시 정지도 여기서 커버된다.
            var sm = SoundManager.Instance;
            if (sm != null)
            {
                if (on) sm.Loop("jetpack", SfxKeys.JetpackThrust);
                else    sm.StopLoop("jetpack");
            }

            if (on && _exhaust == null) BuildExhaust();
            if (_exhaust == null) return;
            if (_exhaustEmission.enabled != on) _exhaustEmission.enabled = on;
        }

        // 플레이어 발밑에 아래로 뿜는 연기 파티클을 코드 생성(프리팹 없음).
        private void BuildExhaust()
        {
            var parent = ctx?.player;
            if (parent == null) return;

            var go = new GameObject("RelicJetpackExhaust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            // 콘 모양 파티클은 로컬 +Z로 방출 → X축 +90° 회전으로 월드 아래(-Y)를 향하게 한다.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _exhaust = go.AddComponent<ParticleSystem>();
            _exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _exhaust.main;
            main.startLifetime   = 0.45f;
            main.startSpeed      = 3.2f;
            main.startSize       = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startColor      = new Color(0.8f, 0.82f, 0.85f, 0.55f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 상승해도 연기는 뒤로 흘림
            main.maxParticles    = 80;

            var emission = _exhaust.emission;
            emission.rateOverTime = 45f;
            emission.enabled      = false;
            _exhaustEmission      = emission;

            var shape = _exhaust.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 12f;
            shape.radius    = 0.12f;

            // 수명에 따라 크기 팽창 + 알파 페이드로 흩어지는 연기 느낌.
            var sol = _exhaust.sizeOverLifetime;
            sol.enabled = true;
            sol.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

            var col = _exhaust.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0f), new GradientColorKey(new Color(0.7f, 0.72f, 0.75f), 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null) renderer.material = new Material(shader);
            renderer.sortingOrder = 50; // 플레이어보다 앞

            _exhaust.Play();
        }
    }
}
