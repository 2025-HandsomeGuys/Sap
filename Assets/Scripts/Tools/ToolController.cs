using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ToolController : MonoBehaviour
{
    public Vector3 followPos;
    public Vector3 scale;
    public Animator playerAnimator;
    public Transform parent;
    public bool isleft;
    public int toolType = 0;

    private Camera cam;

    public float animationMove = 0f;
    public float digRot = 0f;
    public bool digEnd;
    public Vector3 clickVec;
    public Quaternion clickRot;

    public Animator toolAnim;

    Quaternion toolRot = Quaternion.Euler(0, 0, -30);
    Quaternion toolReverseRot = Quaternion.Euler(0, 0, 30);

    public SpriteRenderer Img_Renderer;
    public Sprite sap;
    public Sprite pickaxe;

    //  새로 추가: 따라오는 속도 조절용
    public float followSpeed = 10f;

    void Start()
    {
        cam = Camera.main;
        followPos = parent.position; // 초기화
    }

    void Check()
    {
        scale = parent.localScale;
        isleft = scale.x > 0; //  단순화
    }

    void Follow()
    {
        //  Queue 대신 Lerp 보간
        followPos = Vector3.Lerp(followPos, parent.position, Time.deltaTime * followSpeed);

        Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dirVec = mousePos - (Vector2)transform.position;
        Vector3 animove = clickVec * animationMove;

        if (isleft == true)
        {
            if (toolAnim.GetBool("isdigging") == true)
            {
                transform.position = new Vector3(followPos.x + 0.1f, followPos.y - 0.1f, followPos.z) - animove;
                transform.localRotation = clickRot * Quaternion.Euler(0, 0, digRot);
            }
            else
            {
                transform.position = new Vector3(followPos.x + 0.1f, followPos.y - 0.1f, followPos.z);
            }

            if (dirVec.x < 0 && playerAnimator.GetBool("ismoving") == false && toolAnim.GetBool("isdigging") == false)
            {
                transform.right = -(Vector3)dirVec.normalized;
            }
            else if (playerAnimator.GetBool("ismoving") == true)
            {
                transform.localRotation = toolRot;
            }
        }
        else // 오른쪽
        {
            if (toolAnim.GetBool("isdigging") == true)
            {
                transform.position = new Vector3(followPos.x - 0.1f, followPos.y - 0.1f, followPos.z) + animove;
                transform.localRotation = clickRot * Quaternion.Euler(0, 0, -digRot);
            }
            else
            {
                transform.position = new Vector3(followPos.x - 0.1f, followPos.y - 0.1f, followPos.z);
            }

            if (dirVec.x > 0 && playerAnimator.GetBool("ismoving") == false && toolAnim.GetBool("isdigging") == false)
            {
                transform.right = (Vector3)dirVec.normalized;
            }
            else if (playerAnimator.GetBool("ismoving") == true)
            {
                transform.localRotation = toolReverseRot;
            }
        }
    }

    void Change()
    {
        if (Input.GetAxisRaw("Mouse ScrollWheel") > 0)
        {
            if (toolType == 3)
                toolType = 0;
            else
                toolType += 1;
        }
        else if (Input.GetAxisRaw("Mouse ScrollWheel") < 0)
        {
            if (toolType == 0)
                toolType = 3;
            else
                toolType -= 1;
        }

        if (toolType == 0)
            Img_Renderer.sprite = sap;
        else if (toolType == 1)
            Img_Renderer.sprite = pickaxe;
        else
            Img_Renderer.sprite = pickaxe;
    }

    void Dig()
    {
        Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dirVec = mousePos - (Vector2)transform.position;
        if (Input.GetMouseButtonDown(0) == true && !toolAnim.GetBool("isdigging"))
        {
            clickRot = transform.localRotation;

            if (isleft == true)
                clickVec = -dirVec.normalized;
            else
                clickVec = dirVec.normalized;

            toolAnim.SetTrigger("dig");
            toolAnim.SetBool("isdigging", true);
        }

        if (digEnd == true)
        {
            digEnd = false;
            toolAnim.SetBool("isdigging", false);
        }
    }

    void Update()
    {
        Check();
        //Follow();
        Change();
        Dig();
    }

    void FixedUpdate()
    {
        Follow();
    }
}
