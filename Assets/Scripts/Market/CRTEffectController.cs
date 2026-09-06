// @tags: market, crt, monitor, scanline, overlay, vfx, retro, shader, controller
using UnityEngine;
using UnityEngine.UI;

namespace Market
{
    /// <summary>
    /// 마켓(주식·코인) 단말기 화면에 은은한 브라운관(CRT) 느낌을 입히는 풀스크린 오버레이 컨트롤러.
    /// "UI/CRTOverlay" 셰이더(곱셈 블렌드)로 주사선·새도우마스크·비네팅·플리커를 절차적으로 그린다.
    ///
    /// 사용법: UI 빌드 가이드의 <c>ScanlineOverlay</c>(풀스크린 Image, Raycast Target ✗)에 이 컴포넌트를 붙이면
    /// 끝. 머티리얼을 스스로 생성·연결하므로 별도 .mat 에셋이 필요 없다. 인스펙터 값은 에디터에서 실시간 반영된다.
    ///
    /// 곡률/색수차 등 뒤 콘텐츠 샘플링이 필요한 효과는 Screen Space Overlay에선 불가하므로 제외(설계상 비네팅으로 대체).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class CRTEffectController : MonoBehaviour
    {
        [Header("전체")]
        [Tooltip("끄면 오버레이 자체를 비활성화한다(오버드로 0).")]
        [SerializeField] private bool enableEffect = true;
        [Tooltip("효과 전체 강도. 0이면 영향 없음. 은은하게 0.6~0.8 권장.")]
        [Range(0f, 1f)] [SerializeField] private float intensity = 0.7f;
        [Tooltip("화면 전체 색조. 살짝 초록/호박빛을 주려면 여기서. 기본 흰색=무영향.")]
        [SerializeField] private Color tint = Color.white;

        [Header("주사선")]
        [Tooltip("주사선 한 주기의 디바이스 픽셀 간격. 작을수록 촘촘.")]
        [SerializeField] private float scanPeriod = 3f;
        [Range(0f, 1f)] [SerializeField] private float scanlineDarkness = 0.10f;
        [Range(0.1f, 4f)] [SerializeField] private float scanlineSharpness = 1.4f;

        [Header("새도우 마스크 (RGB 줄무늬)")]
        [Range(0f, 0.3f)] [SerializeField] private float maskStrength = 0.05f;
        [Tooltip("R/G/B 삼색 한 묶음의 픽셀 폭.")]
        [SerializeField] private float maskPeriod = 3f;

        [Header("비네팅")]
        [Range(0f, 1f)] [SerializeField] private float vignette = 0.18f;
        [Range(0.5f, 6f)] [SerializeField] private float vignettePower = 2.5f;

        [Header("플리커")]
        [Range(0f, 0.1f)] [SerializeField] private float flicker = 0.015f;
        [SerializeField] private float flickerSpeed = 8f;

        [Header("롤 밴드 (기본 꺼짐)")]
        [Tooltip("0이면 비활성. 0.02~0.08 정도면 천천히 흐르는 띠.")]
        [SerializeField] private float rollSpeed = 0f;
        [Range(0f, 0.5f)] [SerializeField] private float rollDarkness = 0.06f;
        [Range(0.01f, 0.5f)] [SerializeField] private float rollHeight = 0.08f;

        [Header("셰이더 (비워두면 자동 탐색)")]
        [SerializeField] private Shader crtShader;

        // --- 셰이더 프로퍼티 ID ---
        private static readonly int IdColor             = Shader.PropertyToID("_Color");
        private static readonly int IdIntensity         = Shader.PropertyToID("_Intensity");
        private static readonly int IdScanPeriod        = Shader.PropertyToID("_ScanPeriod");
        private static readonly int IdScanlineDarkness  = Shader.PropertyToID("_ScanlineDarkness");
        private static readonly int IdScanlineSharpness = Shader.PropertyToID("_ScanlineSharpness");
        private static readonly int IdMaskStrength      = Shader.PropertyToID("_MaskStrength");
        private static readonly int IdMaskPeriod        = Shader.PropertyToID("_MaskPeriod");
        private static readonly int IdVignette          = Shader.PropertyToID("_Vignette");
        private static readonly int IdVignettePower     = Shader.PropertyToID("_VignettePower");
        private static readonly int IdFlicker           = Shader.PropertyToID("_Flicker");
        private static readonly int IdFlickerSpeed      = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int IdRollSpeed         = Shader.PropertyToID("_RollSpeed");
        private static readonly int IdRollDarkness      = Shader.PropertyToID("_RollDarkness");
        private static readonly int IdRollHeight        = Shader.PropertyToID("_RollHeight");

        private const string ShaderName = "UI/CRTOverlay";

        private Image _image;
        private Material _runtimeMat;

        // 일시적 연출(대박 시 화면 흔들림 등)용 펄스 상태
        private float _pulseTimer;
        private float _pulseDuration;
        private float _pulseFlicker;
        private float _pulseRollSpeed;

        private void OnEnable()
        {
            EnsureMaterial();
            ApplyToMaterial();
        }

        private void OnDisable()
        {
            // 머티리얼 인스턴스를 비워 누수 방지 (에디터 핫리로드 포함)
            if (_image != null && _image.material == _runtimeMat)
                _image.material = null;
            DestroyRuntimeMaterial();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            // 에디터 실시간 미리보기. 머티리얼이 아직 없으면 만들지는 않는다(OnEnable 담당).
            if (_runtimeMat != null) ApplyToMaterial();
        }
#endif

        private void Update()
        {
            if (_runtimeMat == null) return;
            if (_pulseTimer <= 0f) return;

            _pulseTimer -= Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_pulseTimer / Mathf.Max(_pulseDuration, 0.0001f)); // 1→0 감쇠
            _runtimeMat.SetFloat(IdFlicker, flicker + _pulseFlicker * k);
            _runtimeMat.SetFloat(IdRollSpeed, Mathf.Lerp(rollSpeed, _pulseRollSpeed, k));
            if (_pulseTimer <= 0f)
            {
                // 정상값 복귀
                _runtimeMat.SetFloat(IdFlicker, flicker);
                _runtimeMat.SetFloat(IdRollSpeed, rollSpeed);
            }
        }

        // ===================================================
        // 공개 API
        // ===================================================

        /// <summary>효과 전체 강도(0~1)를 런타임에 조절. 페이드 인/아웃에 사용.</summary>
        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            if (_runtimeMat != null) _runtimeMat.SetFloat(IdIntensity, intensity);
        }

        /// <summary>효과 on/off. 끄면 오버레이 Image 자체를 비활성화한다.</summary>
        public void SetEnabled(bool on)
        {
            enableEffect = on;
            if (_image != null) _image.enabled = on;
        }

        /// <summary>
        /// 짧은 화면 동요(플리커 급증 + 롤 밴드)를 한 번 일으킨다. 코인 떡상/떡락 같은 순간 연출용.
        /// </summary>
        public void Pulse(float duration = 0.5f, float extraFlicker = 0.05f, float rollSpeedBurst = 0.5f)
        {
            _pulseDuration  = Mathf.Max(0.01f, duration);
            _pulseTimer     = _pulseDuration;
            _pulseFlicker   = Mathf.Max(0f, extraFlicker);
            _pulseRollSpeed = rollSpeedBurst;
        }

        // ===================================================
        // 내부
        // ===================================================

        private void EnsureMaterial()
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_image != null) _image.raycastTarget = false;

            var shader = crtShader != null ? crtShader : Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[CRTEffectController] 셰이더 '{ShaderName}'를 찾을 수 없습니다. " +
                               "Assets/Shaders/CRTOverlay.shader 가 있는지 확인하세요.", this);
                return;
            }

            if (_runtimeMat == null || _runtimeMat.shader != shader)
            {
                DestroyRuntimeMaterial();
                _runtimeMat = new Material(shader) { name = "CRTOverlay (instance)", hideFlags = HideFlags.HideAndDontSave };
            }

            if (_image != null)
            {
                _image.material = _runtimeMat;
                _image.enabled = enableEffect;
            }
        }

        private void ApplyToMaterial()
        {
            if (_runtimeMat == null) return;

            _runtimeMat.SetColor(IdColor, tint);
            _runtimeMat.SetFloat(IdIntensity, intensity);

            _runtimeMat.SetFloat(IdScanPeriod, Mathf.Max(1f, scanPeriod));
            _runtimeMat.SetFloat(IdScanlineDarkness, scanlineDarkness);
            _runtimeMat.SetFloat(IdScanlineSharpness, scanlineSharpness);

            _runtimeMat.SetFloat(IdMaskStrength, maskStrength);
            _runtimeMat.SetFloat(IdMaskPeriod, Mathf.Max(1f, maskPeriod));

            _runtimeMat.SetFloat(IdVignette, vignette);
            _runtimeMat.SetFloat(IdVignettePower, vignettePower);

            _runtimeMat.SetFloat(IdFlicker, flicker);
            _runtimeMat.SetFloat(IdFlickerSpeed, flickerSpeed);

            _runtimeMat.SetFloat(IdRollSpeed, rollSpeed);
            _runtimeMat.SetFloat(IdRollDarkness, rollDarkness);
            _runtimeMat.SetFloat(IdRollHeight, rollHeight);

            if (_image != null) _image.enabled = enableEffect;
        }

        private void DestroyRuntimeMaterial()
        {
            if (_runtimeMat == null) return;
            if (Application.isPlaying) Destroy(_runtimeMat);
            else DestroyImmediate(_runtimeMat);
            _runtimeMat = null;
        }
    }
}
