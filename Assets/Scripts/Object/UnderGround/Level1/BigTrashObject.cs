using UnityEngine;
using System.Collections;

/// <summary>
/// 곡괭이 타격 시 80(2개), 60(5개+스프라이트+콜라이더 축소), 40(2개), 20(5개+스프라이트+콜라이더 축소)% 마다 쓰레기를 뿜어내고,
/// 파괴 시 7개를 더 강한 힘으로 뿜어내는 큰 쓰레기 봉투 오브젝트.
/// </summary>
public class BigTrashObject : MonoBehaviour, IDiggable
{
    [Header("상태 설정")]
    [Tooltip("쓰레기 봉투의 최대 체력")]
    public float maxHealth = 50f;
    private float _currentHealth;

    [Header("외형 설정 (스프라이트 변경)")]
    [Tooltip("체력이 60% 이하가 될 때 바뀔 이미지")]
    public Sprite spriteAt60;
    [Tooltip("체력이 20% 이하가 될 때 바뀔 이미지")]
    public Sprite spriteAt20;

    [Header("드롭 설정")]
    [Tooltip("기본 산란 수평 최대 속도")]
    public float scatterSpeedX = 4f;
    [Tooltip("기본 산란 수직 속도 (위로 튀는 힘)")]
    public float scatterSpeedY = 5f;
    [Tooltip("파괴(막타) 시 산란되는 힘의 배수 (예: 1.5 = 1.5배 더 멀리/높게 튐)")]
    public float finalBlastMultiplier = 1.5f;

    [Header("애니메이션")]
    [Tooltip("타격 애니메이션을 재생할 Animator (없으면 자식에서 자동 검색)")]
    public Animator animator;
    [Tooltip("타격 시 재생할 애니메이션(State) 이름")]
    public string hitAnimationName = "TrashHit";

    // 컴포넌트 캐싱
    private Collider2D _collider;
    private SpriteRenderer _spriteRenderer;

    private void Start()
    {
        _currentHealth = maxHealth;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // 컴포넌트 캐싱
        _collider = GetComponent<Collider2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // ─── IDiggable 구현 ───────────────────────────────────────────

    public void Dig(Vector2 hitPoint, float damage, int toolIndex)
    {
        if (_currentHealth <= 0) return;

        // 타격 애니메이션 재생
        if (animator != null && !string.IsNullOrEmpty(hitAnimationName))
        {
            animator.Play(hitAnimationName, -1, 0f);
        }

        float actualDamage = Mathf.Min(damage, _currentHealth);
        if (actualDamage <= 0) return;

        // 타격 전/후의 체력 비율(0.0 ~ 1.0) 계산
        float oldHealthPct = _currentHealth / maxHealth;

        _currentHealth -= actualDamage;

        float newHealthPct = _currentHealth / maxHealth;

        int dropsToSpawn = 0;

        // 80% 구간 통과 체크 (쓰레기 2개)
        if (oldHealthPct >= 0.8f && newHealthPct < 0.8f)
        {
            dropsToSpawn += 2;
        }

        // 60% 구간 통과 체크 (쓰레기 5개 + 60% 이미지로 변경 + 콜라이더 축소)
        if (oldHealthPct >= 0.6f && newHealthPct < 0.6f)
        {
            dropsToSpawn += 5;
            if (_spriteRenderer != null && spriteAt60 != null)
            {
                _spriteRenderer.sprite = spriteAt60;
                AdjustColliderToSprite();
            }
        }

        // 40% 구간 통과 체크 (쓰레기 2개)
        if (oldHealthPct >= 0.4f && newHealthPct < 0.4f)
        {
            dropsToSpawn += 2;
        }

        // 20% 구간 통과 체크 (쓰레기 5개 + 20% 이미지로 변경 + 콜라이더 축소)
        if (oldHealthPct >= 0.2f && newHealthPct < 0.2f)
        {
            dropsToSpawn += 5;
            if (_spriteRenderer != null && spriteAt20 != null)
            {
                _spriteRenderer.sprite = spriteAt20;
                AdjustColliderToSprite();
            }
        }

        // 체력을 다 깎았다면 막타 보너스 7개 추가
        if (_currentHealth <= 0)
        {
            dropsToSpawn += 7;
        }

        // 🔥 막타(체력 0 이하)일 때는 finalBlastMultiplier 적용, 아닐 때는 1배(기본)
        float forceMultiplier = (_currentHealth <= 0) ? finalBlastMultiplier : 1f;

        // 계산된 개수만큼 스폰
        for (int i = 0; i < dropsToSpawn; i++)
        {
            SpawnWithBlast(PickRandomTrash(), forceMultiplier);
        }

        // 체력 소진 시 파괴
        if (_currentHealth <= 0)
        {
            Die();
        }
    }

    // ─── 콜라이더 자동 조절 ───────────────────────────

    private void AdjustColliderToSprite()
    {
        if (_collider == null || _spriteRenderer == null || _spriteRenderer.sprite == null) return;

        Bounds bounds = _spriteRenderer.sprite.bounds;

        if (_collider is BoxCollider2D boxCol)
        {
            boxCol.size = bounds.size;
            boxCol.offset = bounds.center;
        }
        else if (_collider is CapsuleCollider2D capsuleCol)
        {
            capsuleCol.size = bounds.size;
            capsuleCol.offset = bounds.center;
        }
        else if (_collider is CircleCollider2D circleCol)
        {
            circleCol.radius = Mathf.Max(bounds.size.x, bounds.size.y) / 2f;
            circleCol.offset = bounds.center;
        }
    }

    // ─── 파괴 처리 ───────────────────────────────────────────────

    private void Die()
    {
        if (_spriteRenderer != null) _spriteRenderer.enabled = false;
        if (_collider != null) _collider.enabled = false;
        Destroy(gameObject, 0.1f);
    }

    // ─── 산란 스폰 로직 ─────────────────────────────────────────

    private MineralID PickRandomTrash()
    {
        float r = Random.value;
        if (r < 0.80f) return MineralID.GarbageBag;
        if (r < 0.90f) return MineralID.ScrapMetal;
        return MineralID.PETBottle;
    }

    // 파라미터에 forceMultiplier 추가
    private void SpawnWithBlast(MineralID id, float forceMultiplier)
    {
        if (MineralDatabase.Instance == null) return;

        MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
        if (so == null || so.mineralPrefab == null) return;

        Bounds spawnBounds = new Bounds();
        bool hasValidBounds = false;

        if (_spriteRenderer != null && _spriteRenderer.sprite != null)
        {
            spawnBounds = _spriteRenderer.bounds; // 이미지가 바뀌자마자 즉시 갱신된 새로운 크기
            hasValidBounds = true;
        }
        else if (_collider != null)
        {
            spawnBounds = _collider.bounds;
            hasValidBounds = true;
        }

        // 스폰 기준점
        float centerX = hasValidBounds ? spawnBounds.center.x : transform.position.x;
        Vector3 spawnPos;

        if (hasValidBounds)
        {
            spawnPos = new Vector3(
                Random.Range(spawnBounds.min.x, spawnBounds.max.x),
                Random.Range(spawnBounds.min.y, spawnBounds.max.y),
                -1f
            );
        }
        else
        {
            spawnPos = transform.position + new Vector3(
                Random.Range(-0.5f, 0.5f),
                Random.Range(-0.5f, 0.5f),
                -1f
            );
        }

        GameObject spawned = Instantiate(so.mineralPrefab, spawnPos, Quaternion.identity);

        if (spawned.TryGetComponent<MineralItemController>(out var ctrl))
            ctrl.mineralData = so;

        if (spawned.TryGetComponent<Rigidbody2D>(out var rb))
        {
            float offsetX = spawnPos.x - centerX;
            // 코루틴에 배수(forceMultiplier) 전달
            StartCoroutine(ApplyBlastNextFrame(rb, offsetX, forceMultiplier));
        }
    }

    // 튕겨나갈 방향과 힘(forceMultiplier 적용) 결정
    private IEnumerator ApplyBlastNextFrame(Rigidbody2D rb, float offsetX, float forceMultiplier)
    {
        yield return new WaitForFixedUpdate();

        if (rb != null)
        {
            if (rb.bodyType != RigidbodyType2D.Dynamic)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }

            float dirX = (offsetX < 0) ? Random.Range(-1f, -0.2f) : Random.Range(0.2f, 1f);
            Vector2 blastDir = new Vector2(dirX, Random.Range(0.5f, 1f)).normalized;

            // 🔥 기본 속도에 곱해줘서 폭발력을 키웁니다.
            float speedX = Random.Range(scatterSpeedX * 0.5f, scatterSpeedX) * forceMultiplier;
            float speedY = scatterSpeedY * forceMultiplier;

            rb.linearVelocity = new Vector2(blastDir.x * speedX, speedY);
        }
    }
}