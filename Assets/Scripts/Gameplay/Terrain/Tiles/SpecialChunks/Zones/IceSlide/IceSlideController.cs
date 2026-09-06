using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace Gameplay.Terrain.Tiles.SpecialChunks.Zones.IceSlide
{
    /// <summary>
    /// 빙하 슬라이드 구역 메인 컨트롤러.
    /// 플레이어가 진입 시 조작을 비활성화하고, 정해진 웨이포인트(Waypoints)를 따라 고속으로 강제 이동시킵니다.
    /// 이동 중에는 플레이어 캐릭터를 실루엣으로 변경하고 서리(Frost) 필터를 활성화하여 연출을 강화합니다.
    /// </summary>
    public class IceSlideController : MonoBehaviour
    {
        [Header("Slide Path Settings")]
        [Tooltip("플레이어가 강제로 따라갈 곡선 경로 (SplineContainer). 에디터에서 자유롭게 그릴 수 있습니다.")]
        public SplineContainer slidePath;
        [Tooltip("기존 웨이포인트(SplineContainer가 없을 경우 대체로 사용)")]
        public Transform[] waypoints;
        [Tooltip("슬라이딩 고속 하강 속도")]
        public float slideSpeed = 25f;

        [Header("Visual Effects")]
        [Tooltip("슬라이드 중 활성화될 얼음/서리 필터 연출용 게임오브젝트 (Screen Space Post Processing 또는 Local Overlay)")]
        public GameObject frostFilterOverlay;

        private bool _isSliding = false;
        private bool _isPlayerInRange = false;
        private GameObject _playerObj = null;

        private void Update()
        {
            // 설정 오버레이는 E를 '선택' 키로 쓴다 — 열려 있는 동안 월드 상호작용으로 새지 않게 차단.
            if (SettingsOverlayUI.IsOpen) return;

            if (_isPlayerInRange && !_isSliding && InteractionKeys.InteractPressed && _playerObj != null)
            {
                StartCoroutine(SlideRoutine(_playerObj));
            }
        }

        private void OnTriggerEnter2D(Collider2D collision)
        {
            if (collision.CompareTag("Player"))
            {
                _isPlayerInRange = true;
                _playerObj = collision.gameObject;
            }
        }

        private void OnTriggerExit2D(Collider2D collision)
        {
            if (collision.CompareTag("Player"))
            {
                _isPlayerInRange = false;
                if (_playerObj == collision.gameObject)
                {
                    _playerObj = null;
                }
            }
        }

        private IEnumerator SlideRoutine(GameObject playerObj)
        {
            _isSliding = true;

            // 1. 컴포넌트 획득
            PlayerController pc = playerObj.GetComponent<PlayerController>();
            Rigidbody2D rb = playerObj.GetComponent<Rigidbody2D>();
            SpriteRenderer[] playerRenderers = playerObj.GetComponentsInChildren<SpriteRenderer>();

            // 2. 조작 및 물리 연산 차단 (진입 시 처리)
            if (pc != null)
            {
                pc.enabled = false;
                // 슬라이드 중임을 표시하기 위해 특정 애니메이션 파라미터나 상태값을 세팅할 수도 있습니다.
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero; // 기존 관성 제거
            }

            // 3. 비주얼 변경 (플레이어 숨김 및 필터)
            if (playerRenderers != null && playerRenderers.Length > 0)
            {
                foreach (var renderer in playerRenderers)
                {
                    renderer.enabled = false;
                }
            }

            if (frostFilterOverlay != null)
            {
                frostFilterOverlay.SetActive(true);
            }

            // 만약 유저가 인스펙터에서 프리팹 에셋 자체를 끌어다 넣었을 경우를 대비해, 
            // 현재 씬에 생성된(인스턴스화된) 자신의 SplineContainer를 최우선으로 찾아서 사용합니다.
            SplineContainer actualPath = GetComponent<SplineContainer>();
            if (actualPath == null) actualPath = slidePath;

            // 4. 경로 이동 로직 (Path-Following)
            if (actualPath != null)
            {
                // 곡선 꺾임 구간에서 느려지는 현상(Non-uniform parameterization)을 해결하기 위해
                // 거리 기반 룩업 테이블(Lookup Table, LUT)을 생성합니다.
                int sampleCount = 100; // 샘플링이 많을수록 속도가 더 일정해집니다.
                float[] tValues = new float[sampleCount + 1];
                float[] distances = new float[sampleCount + 1];

                tValues[0] = 0f;
                distances[0] = 0f;
                
                float accumulatedDistance = 0f;
                Vector3 prevPos = actualPath.EvaluatePosition(0f);
                prevPos.z = 0f; // 2D 화면상의 실제 거리만 재기 위해 Z축 무시
                
                for (int i = 1; i <= sampleCount; i++)
                {
                    float sampleT = i / (float)sampleCount;
                    Vector3 currentPos = actualPath.EvaluatePosition(sampleT);
                    currentPos.z = 0f; // Z축 무시
                    
                    accumulatedDistance += Vector3.Distance(prevPos, currentPos);
                    
                    tValues[i] = sampleT;
                    distances[i] = accumulatedDistance;
                    
                    prevPos = currentPos;
                }

                float worldLength = accumulatedDistance;
                float distanceTraveled = 0f;
                
                // 플레이어의 원래 Z 위치를 기억해둡니다 (2D 게임에서 Z축으로 날아가는 현상 방지)
                float originalZ = playerObj.transform.position.z;

                if (worldLength > 0.01f)
                {
                    // 2. 스플라인 곡선을 따라 이동합니다.
                    while (distanceTraveled < worldLength)
                    {
                        distanceTraveled += slideSpeed * Time.deltaTime;
                        if (distanceTraveled > worldLength) distanceTraveled = worldLength;
                        
                        // LUT를 사용하여 실제 이동 거리(distanceTraveled)에 해당하는 정확한 t 값을 찾아냅니다.
                        float t = 0f;
                        for (int i = 0; i < sampleCount; i++)
                        {
                            if (distanceTraveled >= distances[i] && distanceTraveled <= distances[i + 1])
                            {
                                float segmentLength = distances[i + 1] - distances[i];
                                float segmentRatio = (segmentLength == 0f) ? 0f : (distanceTraveled - distances[i]) / segmentLength;
                                t = Mathf.Lerp(tValues[i], tValues[i + 1], segmentRatio);
                                break;
                            }
                        }

                        // 일정하게 보정된 t값으로 월드 좌표를 가져옵니다.
                        Vector3 worldPosition = actualPath.EvaluatePosition(t);

                        // 2D 환경이므로 Z축은 원래 플레이어의 Z축 위치로 고정합니다
                        worldPosition.z = originalZ;

                        playerObj.transform.position = worldPosition;
                        yield return null;
                    }
                }
            }
            else if (waypoints != null && waypoints.Length > 0)
            {
                foreach (Transform wp in waypoints)
                {
                    if (wp == null) continue;

                    // 해당 웨이포인트에 도달할 때까지 매 프레임 위치 이동
                    while (Vector3.Distance(playerObj.transform.position, wp.position) > 0.1f)
                    {
                        Vector3 targetPos = wp.position;
                        targetPos.z = playerObj.transform.position.z; // 기존 웨이포인트에서도 Z축 고정

                        playerObj.transform.position = Vector3.MoveTowards(
                            playerObj.transform.position, 
                            targetPos, 
                            slideSpeed * Time.deltaTime
                        );
                        yield return null;
                    }
                }
            }
            else
            {
                Debug.LogWarning("[IceSlideController] 설정된 slidePath 또는 waypoints가 없습니다. 바로 종료 처리를 진행합니다.");
            }

            // 5. 복구 로직 (종료 시 처리)
            if (playerRenderers != null && playerRenderers.Length > 0)
            {
                foreach (var renderer in playerRenderers)
                {
                    renderer.enabled = true;
                }
            }

            if (frostFilterOverlay != null)
            {
                frostFilterOverlay.SetActive(false);
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }

            if (pc != null)
            {
                pc.enabled = true;
            }

            _isSliding = false;
        }
    }
}
