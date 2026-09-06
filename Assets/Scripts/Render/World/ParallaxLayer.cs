using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    public GameObject cam;
    public float parallaxEffect;

    private float startpos;

    void Start()
    {
        startpos = transform.position.x;
    }

    // Update() 대신 LateUpdate()를 사용합니다.
    void LateUpdate()
    {
        // 모든 Update 로직(플레이어 이동, 카메라 이동)이 끝난 후 실행되므로 떨림이 없어집니다.
        float dist = (cam.transform.position.x * parallaxEffect);
        transform.position = new Vector3(startpos + dist, transform.position.y, transform.position.z);
    }
}