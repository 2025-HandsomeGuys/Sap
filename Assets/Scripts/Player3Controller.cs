using UnityEngine;

public class Player3Controller : MonoBehaviour
{
    public Rigidbody2D playerRigidbody;
    public float movePower = 4f;
    public float jumpPower = 10f;

    Animator anim;

    private Camera cam;






    void Start()
    {

        cam = Camera.main;

    }

    void Awake()
    {
        anim = GetComponent<Animator>();
    }
    void OnTriggerStay2D(Collider2D other)
    {
        Debug.Log("a");
        anim.SetBool("isjumping", false);
    }
    void OnTriggerExit2D(Collider2D other)
    {
        Debug.Log("b");
        
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) == true&& !anim.GetBool("isjumping"))
        {
            playerRigidbody.AddForce(Vector3.up * jumpPower, ForceMode2D.Impulse);
            anim.SetTrigger("jump");
            anim.SetBool("isjumping",true);
        }
        Vector3 moveVelocity = Vector3.zero;

        

        if (Input.GetAxisRaw("Horizontal") < 0)
        {
            moveVelocity = Vector3.left;
            transform.localScale = new Vector3(1, 1, 1);
            
            anim.SetBool("ismoving", true);
            
        }

        else if (Input.GetAxisRaw("Horizontal") > 0)
        {
            moveVelocity = Vector3.right;
            transform.localScale = new Vector3(-1, 1, 1);
            
            anim.SetBool("ismoving", true);
            
        }
        else//stop
        {
            Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dirVec = mousePos - (Vector2)transform.position;
            anim.SetBool("ismoving",false);
            if (dirVec.x > 0)
            {
                transform.localScale = new Vector3(-1, 1, 1);
            }
            else if(dirVec.x < 0)
                transform.localScale = new Vector3(1, 1, 1);
        }
       

        transform.position += moveVelocity * movePower * Time.deltaTime;





    }
}
