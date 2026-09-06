using UnityEngine;

public class CloudLayer : MonoBehaviour
{
    [Header("카메라 & 패럴랙스")]
    public GameObject cam;
    [Range(0f, 1f)]
    public float parallaxEffect;

    [Header("구름 이동 설정")]
    public float windSpeed = 0.1f;
    public int numberOfClouds = 3;

    [Header("순간이동 타이밍 조절")]
    [Tooltip("이 값을 늘리면 구름이 화면 왼쪽으로 더 멀리 이동한 뒤에 순간이동합니다.")]
    public float wrapOffset = 5f; // 💡 이 값이 핵심입니다!

    private float length;
    private float startpos;
    private float windOffset;

    void Start()
    {
        startpos = transform.position.x;
        length = GetComponent<SpriteRenderer>().bounds.size.x;
        windOffset = 0f;
    }

    void LateUpdate()
    {
        windOffset += windSpeed * Time.deltaTime;

        float dist = (cam.transform.position.x * parallaxEffect);

        transform.position = new Vector3(startpos + dist - windOffset, transform.position.y, transform.position.z);

        float currentX = transform.position.x;
        float camX = cam.transform.position.x;

        // 💡 순간이동 조건에 wrapOffset(여유값)을 빼주어 더 늦게 순간이동하도록 만듭니다.
        if (currentX < camX - length - wrapOffset)
        {
            // 순간이동하는 '거리' 자체는 구름 크기에 딱 맞춰야 틈새가 안 생깁니다.
            startpos += length * numberOfClouds;
        }
        else if (currentX > camX + length + wrapOffset)
        {
            startpos -= length * numberOfClouds;
        }
    }
}