// @tags: ui, player, preview, live, uiplayer, paperdoll, equipment, code-generated, inventory, warehouse

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 장비/의상이 실시간으로 반영되고 모션까지 나오는 **라이브 플레이어 프리뷰**.
///
/// 페이퍼돌(<see cref="CodePlayerPreview"/>)과 달리 실제 <c>UiPlayer</c> 프리팹을 복제해
/// 전용 카메라로 RenderTexture에 그려 보여준다. 렌더 세부는 <see cref="LivePlayerPreviewRig"/>가 담당하고,
/// 이 클래스는 프리뷰 자리에 상자 + RawImage를 만들고 그 위에 리그 컴포넌트를 붙이는 역할만 한다.
///
/// <see cref="CodeBagPanel"/>이 프리팹을 넘겨받은 경우에만 이 프리뷰를 쓰고,
/// 프리팹이 없으면 기존 페이퍼돌로 폴백한다.
/// </summary>
public class CodeLivePlayerPreview
{
    // 세로로 긴 초상화 비율. 카메라 aspect·RawImage 비율에 함께 쓴다.
    private const int RtWidth = 384;
    private const int RtHeight = 512;

    public RectTransform Root { get; }

    private readonly LivePlayerPreviewRig _rig;

    public CodeLivePlayerPreview(Transform parent, UISkin skin, GameObject prefab, PlayerPreviewSkin config, LocTextBinder loc)
    {
        config ??= new PlayerPreviewSkin();

        var box = CodeUI.CreateImage(parent, "LivePlayerPreview", CodeUI.BoxBg,
            config.backgroundSprite != null ? config.backgroundSprite : skin?.boxSprite, skin);
        Root = box.rectTransform;
        box.gameObject.AddComponent<RectMask2D>();

        // 프리팹이 없으면(=지정 안 됨) 안내 문구만 띄우고 리그는 붙이지 않는다.
        if (prefab == null)
        {
            var warn = CodeUI.CreateText(box.transform, "Empty", 15f, FontStyles.Normal,
                CodeUI.MutedColor, TextAlignmentOptions.Center, loc);
            CodeUI.StretchFull(warn.rectTransform);
            loc?.Bind(warn, "ui_preview_no_prefab", "UiPlayer 프리뷰 프리팹을\n인스펙터에 지정하세요");
            warn.textWrappingMode = TextWrappingModes.Normal;
            return;
        }

        // RenderTexture를 그릴 RawImage — 상자를 빈틈없이 채운다(레터박스/양옆 베젤 없음).
        // 비율 왜곡은 리그가 RT·카메라 aspect를 상자 크기에 맞춰 실시간으로 잡아준다.
        var imgObj = new GameObject("Render", typeof(RectTransform));
        imgObj.transform.SetParent(box.transform, false);
        var raw = imgObj.AddComponent<RawImage>();
        raw.raycastTarget = false;

        var rt = (RectTransform)imgObj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _rig = box.gameObject.AddComponent<LivePlayerPreviewRig>();
        // orthoSize=0 → 리그 기본값 사용. 배경은 상자 색과 맞춰 레터박스가 자연스럽게 이어지게 한다.
        _rig.Configure(prefab, raw, 0f, RtWidth, RtHeight, CodeUI.BoxBg);
    }

    /// <summary>
    /// 실시간 프리뷰라 매 프레임 다시 그릴 필요는 없지만,
    /// 장비 변경 이벤트를 놓치는 경우를 대비해 지금 상태로 한 번 동기화한다.
    /// </summary>
    public void Refresh(EquipmentInventory inventory)
    {
        _rig?.SyncNow();
    }
}
