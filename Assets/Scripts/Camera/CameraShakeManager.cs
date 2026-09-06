using UnityEngine;
using System.Collections;

/// <summary>
/// 카메라 쉐이크 싱글톤.
/// 카메라 GameObject에 부착하면 됩니다.
/// </summary>
public class CameraShakeManager : MonoBehaviour
{
    public static CameraShakeManager Instance;

    private Vector3 _originalLocalPos;
    private Coroutine _shakeCoroutine;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 도구별 사전 정의된 강도로 쉐이크 발동.
    /// toolIndex: 1=삽, 2=곡괭이, 3=드릴
    /// </summary>
    public void ShakeOnDig(int toolIndex)
    {
        switch (toolIndex)
        {
            case 1: Shake(0.08f, 0.04f); break; // 삽
            case 2: Shake(0.12f, 0.07f); break; // 곡괭이
            case 3: Shake(0.05f, 0.02f); break; // 드릴 (연속이라 약하게)
        }
    }

    /// <summary>
    /// 직접 강도 지정 쉐이크.
    /// duration: 지속 시간(초), magnitude: 최대 오프셋(유닛)
    /// </summary>
    public void Shake(float duration, float magnitude)
    {
        if (_shakeCoroutine != null) StopCoroutine(_shakeCoroutine);
        _shakeCoroutine = StartCoroutine(DoShake(duration, magnitude));
    }

    private IEnumerator DoShake(float duration, float magnitude)
    {
        _originalLocalPos = transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // 감쇠: 시간이 지날수록 쉐이크 약해짐
            float strength = Mathf.Lerp(magnitude, 0f, elapsed / duration);
            float x = Random.Range(-1f, 1f) * strength;
            float y = Random.Range(-1f, 1f) * strength;
            transform.localPosition = _originalLocalPos + new Vector3(x, y, 0f);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        transform.localPosition = _originalLocalPos;
        _shakeCoroutine = null;
    }
}
