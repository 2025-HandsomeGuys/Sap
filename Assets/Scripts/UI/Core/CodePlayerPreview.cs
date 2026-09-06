// @tags: ui, player, preview, paperdoll, equipment, code-generated, inventory, warehouse

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인스펙터에서 지정하는 플레이어 프리뷰 아트 묶음.
/// </summary>
[System.Serializable]
public class PlayerPreviewSkin
{
    [Tooltip("장비를 아무것도 착용하지 않은 기본 플레이어 모습")]
    public Sprite baseSprite;

    [Tooltip("프리뷰 영역을 몇 %까지 채울지 (1에 가까울수록 크게)")]
    [Range(0.3f, 1f)] public float fillRatio = 0.95f;

    [Tooltip("프리뷰 배경 스프라이트 (비우면 어두운 상자만)")]
    public Sprite backgroundSprite;

    [Tooltip("도트 아트가 뭉개져 보이면 켤 것 — 스프라이트를 픽셀 단위로 딱 떨어지게 그린다")]
    public bool pixelPerfect = true;
}

/// <summary>
/// 장비 착용 상태를 보여주는 **페이퍼돌** 프리뷰.
///
/// 실제 플레이어 오브젝트를 흉내내지 않는다 — 점프·벽타기 같은 동작 포즈나
/// 시야 오버레이·손전등 같은 부수 스프라이트가 섞이지 않도록,
/// '기본 모습' 스프라이트 위에 '장비별 외형' 스프라이트만 겹쳐 그린다.
///
/// 장비 외형은 <see cref="EquipmentSO.previewSprite"/>를 쓴다. 비어 있으면 그 층은 건너뛴다.
/// 모든 층은 같은 사각형에 그려지므로, **아트를 같은 캔버스 크기로 그리면 위치가 저절로 맞는다.**
/// </summary>
public class CodePlayerPreview
{
    // 겹쳐 그리는 순서 (뒤 → 앞). 유물은 장신구라 맨 위.
    private static int DrawPriority(EquipmentType type)
    {
        switch (type)
        {
            case EquipmentType.Shoes: return 1;
            case EquipmentType.Clothes: return 2;
            case EquipmentType.Head: return 3;
            case EquipmentType.Relic: return 4;
            default: return 5;
        }
    }

    private readonly PlayerPreviewSkin _config;
    private readonly RectTransform _stage;   // 실제 그림이 놓이는 정사각 영역
    private readonly Image _baseImage;
    private readonly List<Image> _layers = new List<Image>();
    private readonly TextMeshProUGUI _emptyLabel;

    private readonly List<(Sprite sprite, int order)> _pending = new List<(Sprite, int)>();

    public RectTransform Root { get; }

    /// <summary>프리뷰 영역을 만든다. parent의 레이아웃이 크기를 정하고, 그림은 그 안에 꽉 차게 들어간다.</summary>
    public CodePlayerPreview(Transform parent, UISkin skin, PlayerPreviewSkin config, LocTextBinder loc)
    {
        _config = config ?? new PlayerPreviewSkin();

        var box = CodeUI.CreateImage(parent, "PlayerPreview", CodeUI.BoxBg,
            _config.backgroundSprite != null ? _config.backgroundSprite : skin?.boxSprite, skin);
        Root = box.rectTransform;
        box.gameObject.AddComponent<RectMask2D>();

        // 그림이 놓이는 영역 — fillRatio 만큼 안쪽으로 들어간 사각형
        _stage = CodeUI.CreateRect(box.transform, "Stage");
        _stage.anchorMin = _stage.anchorMax = new Vector2(0.5f, 0.5f);
        _stage.pivot = new Vector2(0.5f, 0.5f);

        _baseImage = CodeUI.CreateImage(_stage, "Base", Color.white, rounded: false);
        StretchLayer(_baseImage);

        _emptyLabel = CodeUI.CreateText(box.transform, "Empty", 15f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, loc);
        CodeUI.StretchFull(_emptyLabel.rectTransform);
        loc?.Bind(_emptyLabel, "ui_preview_no_art", "플레이어 기본 모습 스프라이트를\n인스펙터에 지정하세요");
        _emptyLabel.textWrappingMode = TextWrappingModes.Normal;
    }

    private void StretchLayer(Image img)
    {
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    /// <summary>
    /// 현재 장착 상태를 다시 그린다. 매 프레임 불러도 되지만, 장비가 바뀔 때만 불러도 충분하다.
    /// </summary>
    public void Refresh(EquipmentInventory inventory)
    {
        bool hasBase = _config.baseSprite != null;
        _baseImage.sprite = _config.baseSprite;
        _baseImage.enabled = hasBase;
        _emptyLabel.gameObject.SetActive(!hasBase);

        LayoutStage();

        // ── 장착된 장비 중 외형 스프라이트가 있는 것만 모아 순서대로 ──
        _pending.Clear();
        if (hasBase && inventory != null)
        {
            var list = inventory.ReadonlyItems;
            for (int i = 0; i < list.Count; i++)
            {
                var equipment = list[i]?.item as EquipmentSO;
                if (equipment == null || equipment.previewSprite == null) continue;
                _pending.Add((equipment.previewSprite, DrawPriority(equipment.equipmentType)));
            }
            _pending.Sort((a, b) => a.order.CompareTo(b.order));
        }

        while (_layers.Count < _pending.Count)
        {
            var img = CodeUI.CreateImage(_stage, $"Layer_{_layers.Count}", Color.white, rounded: false);
            StretchLayer(img);
            _layers.Add(img);
        }

        for (int i = 0; i < _layers.Count; i++)
        {
            bool used = i < _pending.Count;
            _layers[i].gameObject.SetActive(used);
            if (!used) continue;

            _layers[i].sprite = _pending[i].sprite;
            _layers[i].transform.SetAsLastSibling(); // 정렬 순서대로 위로 쌓는다
        }
    }

    /// <summary>영역 안에서 그림이 놓일 정사각형을 잡는다(짧은 변 기준 + fillRatio).</summary>
    private void LayoutStage()
    {
        Rect area = ((RectTransform)_stage.parent).rect;
        float side = Mathf.Max(1f, Mathf.Min(area.width, area.height)) * Mathf.Clamp01(_config.fillRatio);

        if (_config.pixelPerfect) side = Mathf.Floor(side);
        _stage.sizeDelta = new Vector2(side, side);
    }
}
