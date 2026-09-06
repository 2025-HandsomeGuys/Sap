using UnityEngine;
using UnityEngine.U2D.IK;

public class TwoPointFootIK : MonoBehaviour
{
    [Header("왼발 세팅")]
    public Solver2D leftHeelSolver;
    public Transform leftHeelBone;   // 왼쪽 발꿈치 뼈 끝점
    public Transform leftHeelOrigin; // 레이저 쏠 위치 (무릎 높이)

    public Solver2D leftToeSolver;
    public Transform leftToeBone;    // 왼쪽 발가락 뼈 끝점
    public Transform leftToeOrigin;  // 레이저 쏠 위치 (무릎 높이)

    [Header("오른발 세팅")]
    public Solver2D rightHeelSolver;
    public Transform rightHeelBone;
    public Transform rightHeelOrigin;

    public Solver2D rightToeSolver;
    public Transform rightToeBone;
    public Transform rightToeOrigin;

    [Header("설정")]
    public LayerMask groundLayer;
    public float rayDistance = 1.5f; // Origin 위치에서부터 쏠 레이저 길이 (충분히 길게)
    public float footOffset = 0.05f; // 발바닥 두께
    public float smoothSpeed = 25f;  // 반응 속도

    // 내부 변수 (타겟들)
    private Transform lHeelTarget, lToeTarget, rHeelTarget, rToeTarget;
    // 부드러운 전환을 위한 가중치 변수들
    private float lHeelWeight, lToeWeight, rHeelWeight, rToeWeight;

    void Start()
    {
        // 솔버에서 타겟을 자동으로 찾아옵니다.
        lHeelTarget = leftHeelSolver.GetChain(0).target;
        lToeTarget = leftToeSolver.GetChain(0).target;
        rHeelTarget = rightHeelSolver.GetChain(0).target;
        rToeTarget = rightToeSolver.GetChain(0).target;
    }

    void LateUpdate()
    {
        // 4개의 점(타겟)을 각각 개별적으로 연산하여 바닥으로 끌어당깁니다.
        SolvePoint(leftHeelSolver, lHeelTarget, leftHeelBone, leftHeelOrigin, ref lHeelWeight);
        SolvePoint(leftToeSolver, lToeTarget, leftToeBone, leftToeOrigin, ref lToeWeight);
        SolvePoint(rightHeelSolver, rHeelTarget, rightHeelBone, rightHeelOrigin, ref rHeelWeight);
        SolvePoint(rightToeSolver, rToeTarget, rightToeBone, rightToeOrigin, ref rToeWeight);
    }

    void SolvePoint(Solver2D solver, Transform target, Transform boneEnd, Transform rayOrigin, ref float currentWeight)
    {
        // 허공에 띄워둔 Origin에서 아래쪽으로 레이저 발사
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin.position, Vector2.down, rayDistance, groundLayer);

        // [기본값] IK 끄기 (애니메이션 100% 사용)
        Vector3 targetPos = boneEnd.position;
        float targetWeight = 0f;

        if (hit.collider != null)
        {
            // 현재 뼈 위치가 바닥(hit.point.y) + 여유분 보다 낮아져서 파묻히려고 할 때만 IK 켬
            if (boneEnd.position.y < hit.point.y + footOffset + 0.1f)
            {
                // 위치: 타겟의 x좌표는 유지하고, y좌표만 땅 표면으로 강제 고정
                targetPos = new Vector3(target.position.x, hit.point.y + footOffset, target.position.z);
                targetWeight = 1f;
            }
        }

        // 적용 (부드럽게 이동)
        target.position = Vector3.Lerp(target.position, targetPos, Time.deltaTime * smoothSpeed);
        currentWeight = Mathf.Lerp(currentWeight, targetWeight, Time.deltaTime * smoothSpeed);
        solver.weight = currentWeight;
    }

    // 에디터 씬 뷰에서 레이저가 잘 쏴지는지 빨간 선으로 보여줍니다. (길이나 위치 조절할 때 확인용)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (leftHeelOrigin) Gizmos.DrawLine(leftHeelOrigin.position, leftHeelOrigin.position + Vector3.down * rayDistance);
        if (leftToeOrigin) Gizmos.DrawLine(leftToeOrigin.position, leftToeOrigin.position + Vector3.down * rayDistance);
        if (rightHeelOrigin) Gizmos.DrawLine(rightHeelOrigin.position, rightHeelOrigin.position + Vector3.down * rayDistance);
        if (rightToeOrigin) Gizmos.DrawLine(rightToeOrigin.position, rightToeOrigin.position + Vector3.down * rayDistance);
    }
}