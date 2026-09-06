using System.Collections;
using TMPro;
using UnityEngine;

namespace Sap.UI.Notification
{
    [RequireComponent(typeof(CanvasGroup))]
    public class NotificationUI : MonoBehaviour
    {
        public static NotificationUI Instance { get; private set; }

        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI notificationText;

        [Header("Animation Settings")]
        [SerializeField] private float fadeInDuration = 0.5f;
        [SerializeField] private float displayDuration = 2.0f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        [Header("Test")]
        [SerializeField] private string testMessage = "이것은 테스트 알림입니다!";

        private Coroutine _fadeCoroutine;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            // 초기 상태 설정
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 알림 메시지를 화면에 표시합니다.
        /// </summary>
        /// <param name="message">표시할 텍스트</param>
        public void ShowNotification(string message)
        {
            if (notificationText != null)
            {
                notificationText.text = message;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            // 활성화되어 있지 않다면 활성화
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            _fadeCoroutine = StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            // 1. Fade In
            float timer = 0f;
            float startAlpha = canvasGroup.alpha;
            while (timer < fadeInDuration)
            {
                timer += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, timer / fadeInDuration);
                yield return null;
            }
            canvasGroup.alpha = 1f;

            // 2. Wait
            yield return new WaitForSeconds(displayDuration);

            // 3. Fade Out
            timer = 0f;
            while (timer < fadeOutDuration)
            {
                timer += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeOutDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;

            _fadeCoroutine = null;
        }

        [ContextMenu("Test Show Notification")]
        private void TestShowNotification()
        {
            if (Application.isPlaying)
            {
                ShowNotification(testMessage);
            }
            else
            {
                Debug.LogWarning("[NotificationUI] 알림 테스트는 플레이 모드(Play Mode)에서만 가능합니다.");
            }
        }
    }
}
