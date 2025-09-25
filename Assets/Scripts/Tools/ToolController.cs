
using UnityEngine;

public class ToolController : MonoBehaviour
{
    [Header("Visuals & Animation")]
    public Animator playerAnimator;
    public Transform parent;
    public Animator toolAnim;
    public SpriteRenderer Img_Renderer;
    public Sprite sap;
    public Sprite pickaxe;
    public float followSpeed = 10f;

    [Header("Logic (Dependencies)")]
    [Tooltip("Hierarchy 창에서 Player 오브젝트를 이곳으로 끌어다 놓으세요.")]
    public DiggingController diggingController;

    // Private internal state
    private Camera cam;
    private bool isleft;

    // Animation-related internal state
    public float animationMove = 0f;
    public float digRot = 0f;
    public bool digEnd;
    private Vector3 clickVec;
    private Quaternion clickRot;
    private int toolType = 0;
    private Vector3 followPos;
    private Vector3 scale;
    private Quaternion toolRot = Quaternion.Euler(0, 0, -30);
    private Quaternion toolReverseRot = Quaternion.Euler(0, 0, 30);

    void Start()
    {
        cam = Camera.main;
        if (parent != null) 
        {
            followPos = parent.position;
        }

        // Check if the DiggingController has been assigned in the inspector.
        if (diggingController == null)
        {
            Debug.LogError("'ToolController'의 'Digging Controller' 필드가 비어있습니다! 인스펙터에서 Player 오브젝트를 끌어다 할당해주세요.", this.gameObject);
        }
    }

    void Update()
    {
        CheckVisualState();
        HandleToolChange();
        HandleDigging();
    }

    void FixedUpdate()
    {
        FollowParent();
    }

    void CheckVisualState()
    {
        if (parent == null) return;
        scale = parent.localScale;
        isleft = scale.x > 0;
    }

    void HandleToolChange()
    {
        if (Input.GetAxisRaw("Mouse ScrollWheel") > 0)
        {
            toolType = (toolType + 1) % 4; // Assuming 4 tool types 0,1,2,3
        }
        else if (Input.GetAxisRaw("Mouse ScrollWheel") < 0)
        {
            if (toolType == 0) toolType = 3;
            else toolType -= 1;
        }

        if (toolType == 0) Img_Renderer.sprite = sap;
        else if (toolType == 1) Img_Renderer.sprite = pickaxe;
        else Img_Renderer.sprite = pickaxe; // Placeholder for other tools
    }

    void HandleDigging()
    {
        if (cam == null) return;

        // 1. Determine dig direction from mouse position
        Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 currentDigDirection = (mousePos - (Vector2)transform.position).normalized;

        // 2. Trigger animation on mouse down (works regardless of DiggingController)
        if (Input.GetMouseButtonDown(0) && toolAnim != null && !toolAnim.GetBool("isdigging"))
        {
            clickRot = transform.localRotation;
            clickVec = isleft ? -currentDigDirection : currentDigDirection;
            toolAnim.SetTrigger("dig");
            toolAnim.SetBool("isdigging", true);
        }

        // 3. Command DiggingController while mouse is held down
        if (Input.GetMouseButton(0))
        {
            Debug.Log($"[ToolController] Digging direction: {currentDigDirection}");
            if (diggingController != null)
            {
                diggingController.ExecuteDig(currentDigDirection);
            }
        }

        // 4. End animation
        if (digEnd && toolAnim != null)
        {
            digEnd = false;
            toolAnim.SetBool("isdigging", false);
        }
    }

    void FollowParent()
    {
        if (parent == null) return;

        followPos = Vector3.Lerp(followPos, parent.position, Time.deltaTime * followSpeed);
        Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dirVec = mousePos - (Vector2)transform.position;
        Vector3 animove = clickVec * animationMove;

        if (isleft)
        {
            transform.position = new Vector3(followPos.x + 0.1f, followPos.y - 0.1f, followPos.z) - animove;
            if (toolAnim != null && toolAnim.GetBool("isdigging"))
            {
                transform.localRotation = clickRot * Quaternion.Euler(0, 0, digRot);
            }
            else if (playerAnimator != null && !playerAnimator.GetBool("ismoving"))
            {
                if(dirVec.x < 0) transform.right = -(Vector3)dirVec.normalized;
            }
            else
            {
                transform.localRotation = toolRot;
            }
        }
        else // Right
        {
            transform.position = new Vector3(followPos.x - 0.1f, followPos.y - 0.1f, followPos.z) + animove;
            if (toolAnim != null && toolAnim.GetBool("isdigging"))
            {
                transform.localRotation = clickRot * Quaternion.Euler(0, 0, -digRot);
            }
            else if (playerAnimator != null && !playerAnimator.GetBool("ismoving"))
            {
                if(dirVec.x > 0) transform.right = (Vector3)dirVec.normalized;
            }
            else
            {
                transform.localRotation = toolReverseRot;
            }
        }
    }
}
