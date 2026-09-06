// @tags: guide, tutorial, ui, data, scriptable-object, media, video, page

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 한 페이지가 보여줄 미디어 종류.
/// None = 미디어 없이 텍스트만(코드 생성 플레이스홀더가 대신 뜬다).
/// </summary>
public enum GuideMediaType
{
    None,   // 텍스트만
    Image,  // 정지 이미지 (Sprite)
    Video,  // 동영상 (VideoClip — "gif"는 짧은 루프 mp4/webm로 임포트해 여기에 넣는다)
    Frames  // 스프라이트 프레임 애니메이션 (픽셀 아트 gif 대체용)
}

/// <summary>
/// 가이드 한 페이지 = 미디어(위) + 제목/본문(아래). 세피리아 가이드의 한 장에 해당.
/// 텍스트는 Localization 키 + 폴백(키 미등록 시 표시할 한국어) 쌍으로 둔다.
/// </summary>
[Serializable]
public class GuidePage
{
    [Header("미디어")]
    public GuideMediaType mediaType = GuideMediaType.None;

    [Tooltip("mediaType=Image 일 때 표시할 정지 이미지")]
    public Sprite image;

    [Tooltip("mediaType=Video 일 때 재생할 동영상. gif는 짧은 루프 동영상으로 변환해 넣는다")]
    public VideoClip video;

    [Tooltip("mediaType=Frames 일 때 순서대로 재생할 스프라이트들")]
    public Sprite[] frames;

    [Tooltip("Frames 재생 속도 (초당 프레임)")]
    [Range(1f, 60f)] public float framesPerSecond = 8f;

    [Header("텍스트")]
    [Tooltip("제목 Localization 키 (예: ui_guide_controls_move_title). 비우면 폴백만 사용")]
    public string titleKey;
    [Tooltip("Localization 미등록 시 표시할 제목")]
    public string titleFallback;

    [Tooltip("본문 Localization 키 (예: ui_guide_controls_move_body)")]
    public string bodyKey;
    [Tooltip("Localization 미등록 시 표시할 본문")]
    [TextArea(2, 6)] public string bodyFallback;

    /// <summary>이 페이지가 실제로 재생할 미디어가 있는지(플레이스홀더 판단용).</summary>
    public bool HasMedia
    {
        get
        {
            switch (mediaType)
            {
                case GuideMediaType.Image:  return image != null;
                case GuideMediaType.Video:  return video != null;
                case GuideMediaType.Frames: return frames != null && frames.Length > 0;
                default: return false;
            }
        }
    }
}

/// <summary>
/// 가이드 한 개 = 여러 페이지. 새로운 것을 접했을 때(조작키·유물·도구·청크 등)
/// 게임을 멈추고 이 가이드를 페이지 단위로 넘겨 보여준다.
///
/// 추가하는 법: 이 SO 에셋을 <b>Resources/Guides/</b> 아래에 만들면
/// GuideManager 가 자동으로 로드·등록한다(씬/코드 수정 불필요).
/// 삭제도 에셋만 지우면 된다. 코드에서 <c>GuideManager.Trigger(guideId)</c> 로 띄운다.
/// </summary>
[CreateAssetMenu(fileName = "Guide_", menuName = "Guide/Guide", order = 0)]
public class GuideSO : ScriptableObject
{
    [Tooltip("가이드 고유 id. 코드에서 GuideManager.Trigger(\"이 값\")으로 띄운다. 소문자+언더스코어 권장")]
    public string guideId;

    [Tooltip("체크하면 한 번 본 뒤로는 다시 자동으로 뜨지 않는다(seenGuideIds에 기록). " +
             "해제하면 조건이 맞을 때마다 매번 뜬다")]
    public bool showOnce = true;

    [Tooltip("가이드 상단 배너에 항상 표시할 제목(페이지별 제목 대신). 비우면 각 페이지 titleKey/폴백 사용")]
    public string bannerTitleKey;
    public string bannerTitleFallback;

    [Tooltip("페이지 목록 — 위에서부터 순서대로 넘긴다")]
    public List<GuidePage> pages = new List<GuidePage>();

    [Header("연계 액션")]
    [Tooltip("가이드 창을 닫았을 때 이어서 보여줄 대화 데이터")]
    public DialogueData nextDialogue;
    [Tooltip("가이드 창이 닫힌 뒤 대화 창이 뜨기까지의 딜레이(초)")]
    public float nextDialogueDelay = 0.5f;

    /// <summary>페이지가 하나라도 있는 유효한 가이드인지.</summary>
    public bool IsValid => !string.IsNullOrEmpty(guideId) && pages != null && pages.Count > 0;
}
