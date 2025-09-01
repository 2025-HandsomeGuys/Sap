
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Text;
using System.Threading.Tasks;

// Gemini API 요청 본문을 위한 클래스
[System.Serializable]
public class GeminiRequest
{
    public Content[] contents;
}

[System.Serializable]
public class Content
{
    public Part[] parts;
}

[System.Serializable]
public class Part
{
    public string text;
}

// Gemini API 응답을 파싱하기 위한 클래스
[System.Serializable]
public class GeminiResponse
{
    public Candidate[] candidates;
}

[System.Serializable]
public class Candidate
{
    public Content content;
}


public class GeminiWindow : EditorWindow
{
    private string apiKey = "";
    private string userPrompt = "이 게임에 맞는 새로운 기능 아이디어를 줘.";
    private string geminiResponse = "여기에 Gemini의 답변이 표시됩니다...";
    private Vector2 scrollPosition;
    private bool isWaitingForResponse = false;

    // 메뉴 아이템을 추가하여 에디터 창을 열 수 있게 함
    [MenuItem("Window/Gemini")]
    public static void ShowWindow()
    {
        GetWindow<GeminiWindow>("Gemini");
    }

    private void OnEnable()
    {
        // 저장된 API 키가 있으면 불러오기
        apiKey = EditorPrefs.GetString("GeminiApiKey", "");
    }

    private void OnGUI()
    {
        GUILayout.Label("Gemini API 연동", EditorStyles.boldLabel);

        // API 키 입력 필드
        EditorGUI.BeginChangeCheck();
        apiKey = EditorGUILayout.PasswordField("API Key", apiKey);
        if (EditorGUI.EndChangeCheck())
        {
            // API 키가 변경되면 EditorPrefs에 저장
            EditorPrefs.SetString("GeminiApiKey", apiKey);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            EditorGUILayout.HelpBox("Google AI Studio에서 발급받은 API 키를 입력하세요.", MessageType.Info);
        }

        EditorGUILayout.Space();

        // 사용자 질문 입력 필드
        GUILayout.Label("질문 입력", EditorStyles.label);
        userPrompt = EditorGUILayout.TextArea(userPrompt, GUILayout.Height(100));

        // API 호출 버튼
        if (GUILayout.Button("Send") && !isWaitingForResponse)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                EditorUtility.DisplayDialog("API 키 필요", "Gemini API 키를 먼저 입력해주세요.", "확인");
                return;
            }
            CallGeminiAPI();
        }

        // 응답 대기 중 표시
        if (isWaitingForResponse)
        {
            EditorGUILayout.HelpBox("Gemini의 답변을 기다리는 중입니다...", MessageType.Info);
        }

        EditorGUILayout.Space();

        // Gemini 응답 표시 영역
        GUILayout.Label("Gemini 응답", EditorStyles.label);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
        EditorGUILayout.SelectableLabel(geminiResponse, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private async void CallGeminiAPI()
    {
        isWaitingForResponse = true;
        geminiResponse = "요청 처리 중...";
        Repaint(); // UI 갱신

        // Gemini API 엔드포인트
        string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro-latest:generateContent?key=" + apiKey;

        // 요청 본문 생성
        var requestData = new GeminiRequest
        {
            contents = new Content[]
            {
                new Content { parts = new Part[] { new Part { text = userPrompt } } }
            }
        };
        string jsonBody = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        // UnityWebRequest 생성 및 설정
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // 비동기적으로 요청 보내기
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            // 결과 처리
            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                GeminiResponse response = JsonUtility.FromJson<GeminiResponse>(jsonResponse);

                // 응답 구조를 안전하게 확인하고 텍스트 추출
                if (response != null && response.candidates != null && response.candidates.Length > 0 &&
                    response.candidates[0].content != null && response.candidates[0].content.parts != null && response.candidates[0].content.parts.Length > 0)
                {
                    geminiResponse = response.candidates[0].content.parts[0].text;
                }
                else
                {
                    geminiResponse = "오류: 유효한 응답을 파싱할 수 없습니다.\n" + jsonResponse;
                }
            }
            else
            {
                geminiResponse = "오류: " + request.error + "\n" + request.downloadHandler.text;
            }
        }

        isWaitingForResponse = false;
        Repaint(); // UI 갱신
    }
}
