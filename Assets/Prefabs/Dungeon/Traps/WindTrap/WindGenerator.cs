using UnityEngine;
using System.Collections.Generic; // List를 사용하기 위해 추가해야 합니다.

[RequireComponent(typeof(BoxCollider2D))]
public class WindGenerator : MonoBehaviour
{
    [Header("바람 설정")]
    public int windLength = 5;
    public float windForce = 15f;

    [Header("시각 효과")]
    public GameObject windPrefab;

    private BoxCollider2D windZone;

    // 생성된 바람 프리팹들만 따로 저장해둘 리스트
    private List<GameObject> spawnedWinds = new List<GameObject>();

    private void Awake()
    {
        SetupWindZone();
    }

    private void Start()
    {
        CreateWindVisuals();
    }

    private void OnValidate()
    {
        SetupWindZone();
    }

    private void SetupWindZone()
    {
        if (windZone == null) windZone = GetComponent<BoxCollider2D>();

        if (windZone != null)
        {
            windZone.isTrigger = true;
            windZone.size = new Vector2(windLength, 1f);
            windZone.offset = new Vector2(windLength / 2f, 0f);
        }
    }

    private void CreateWindVisuals()
    {
        if (windPrefab == null)
        {
            Debug.LogWarning("바람 프리팹이 할당되지 않았습니다!");
            return;
        }

        // 1. 우리가 이 스크립트에서 직접 생성했던 바람들만 골라서 삭제합니다.
        foreach (GameObject wind in spawnedWinds)
        {
            if (wind != null)
            {
                Destroy(wind);
            }
        }
        spawnedWinds.Clear(); // 리스트 비우기

        // 2. 새로운 바람 생성
        for (int i = 0; i < windLength; i++)
        {
            GameObject windSprite = Instantiate(windPrefab, transform);
            windSprite.transform.localPosition = new Vector2(i + 0.5f, 0f);

            // 생성한 바람을 리스트에 추가하여 나중에도 기억하도록 합니다.
            spawnedWinds.Add(windSprite);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            Rigidbody2D rb = other.GetComponent<Rigidbody2D>();

            if (rb != null)
            {
                rb.AddForce(transform.right * windForce, ForceMode2D.Force);
            }
        }
    }
}