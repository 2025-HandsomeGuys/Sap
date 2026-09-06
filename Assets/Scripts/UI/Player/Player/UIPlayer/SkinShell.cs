using UnityEngine;
using UnityEngine.U2D.Animation;

public class SkinShell : MonoBehaviour
{
    [Header("추적할 플레이어 본체의 SkinManager")]
    public SkinManager targetPlayer;

    [Header("껍데기(자신)의 부위별 라이브러리 컴포넌트")]
    public SpriteLibrary hairSpriteLibrary;
    public SpriteLibrary bodySpriteLibrary;
    public SpriteLibrary leftShoeSpriteLibrary;
    public SpriteLibrary rightShoeSpriteLibrary;

    private void OnEnable()
    {
        if (targetPlayer != null)
        {
            // 플레이어 옷 변경 이벤트 구독
            targetPlayer.OnSkinChanged += SyncWithPlayer;
            // 활성화될 때 현재 플레이어 입고 있는 옷으로 즉시 동기화
            SyncWithPlayer();
        }
    }

    private void OnDisable()
    {
        if (targetPlayer != null)
        {
            // 오브젝트가 꺼지거나 파괴될 때 이벤트 구독 해제 (메모리 누수 방지)
            targetPlayer.OnSkinChanged -= SyncWithPlayer;
        }
    }

    /// <summary>
    /// 플레이어 본체의 현재 SpriteLibraryAsset을 그대로 가져와 적용합니다.
    /// </summary>
    public void SyncWithPlayer()
    {
        if (targetPlayer == null) return;

        // 본체의 각 부위별 적용된 에셋을 그대로 내 부위에 덮어씌움
        if (hairSpriteLibrary != null && targetPlayer.hairSpriteLibrary != null)
            hairSpriteLibrary.spriteLibraryAsset = targetPlayer.hairSpriteLibrary.spriteLibraryAsset;

        if (bodySpriteLibrary != null && targetPlayer.bodySpriteLibrary != null)
            bodySpriteLibrary.spriteLibraryAsset = targetPlayer.bodySpriteLibrary.spriteLibraryAsset;

        if (leftShoeSpriteLibrary != null && targetPlayer.leftShoeSpriteLibrary != null)
            leftShoeSpriteLibrary.spriteLibraryAsset = targetPlayer.leftShoeSpriteLibrary.spriteLibraryAsset;

        if (rightShoeSpriteLibrary != null && targetPlayer.rightShoeSpriteLibrary != null)
            rightShoeSpriteLibrary.spriteLibraryAsset = targetPlayer.rightShoeSpriteLibrary.spriteLibraryAsset;
    }
}