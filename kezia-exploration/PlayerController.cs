using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 8f;
    public float slideSpeed = 5f;
    public float jumpSpeed = 7f;
    public float turnSpeed = 120f;

    // ---- Agent control (filled in by BugsyAgent) ----
    public bool agentControlled = false;
    public float agentMoveF;      // -1 back, 0 none, 1 forward
    public float agentMoveS;      // -1 left, 0 none, 1 right
    public float agentTurn;       // -1 turn left, 0 none, 1 turn right
    public bool agentJump;
    public bool agentSlide;

    Rigidbody rb;
    bool isGrounded;
    bool isSliding;
    float inputF;
    float inputS;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Stronger collision response, so moving obstacles push the player out
        // instead of letting it sink into them
        rb.maxDepenetrationVelocity = 25f;
        rb.solverIterations = 12;
        rb.solverVelocityIterations = 4;
    }

    void Update()
    {
        bool wantSlide;
        bool wantJump;

        if (agentControlled)
        {
            inputF = agentMoveF;
            inputS = agentMoveS;
            wantSlide = agentSlide;
            wantJump = agentJump;
        }
        else
        {
            // Read input every frame so it never gets missed
            inputF = Input.GetAxisRaw("Vertical");
            inputS = Input.GetAxisRaw("Horizontal");
            wantSlide = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.C);
            wantJump = Input.GetKeyDown(KeyCode.Space);
        }

        if (wantSlide && isGrounded && !isSliding)
        {
            SetSlide(true);
        }
        else if (!wantSlide && isSliding && HasHeadroom())
        {
            SetSlide(false);
        }

        if (wantJump && isGrounded && !isSliding)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpSpeed, rb.linearVelocity.z);
        }
    }

    void FixedUpdate()
    {
        float half = isSliding ? 0.5f : 1f;
        isGrounded = Physics.Raycast(transform.position, Vector3.down, half + 0.1f);

        Vector3 fwd = Vector3.forward;
        Vector3 right = Vector3.right;

        if (agentControlled)
        {
            // Turn, then move relative to the player's own facing direction
            transform.Rotate(0f, agentTurn * turnSpeed * Time.fixedDeltaTime, 0f);
            Vector3 f = transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f)
            {
                fwd = f.normalized;
                right = new Vector3(fwd.z, 0f, -fwd.x);
            }
        }
        else
        {
            // Move relative to the camera direction
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.001f)
                {
                    fwd = f.normalized;
                    right = new Vector3(fwd.z, 0f, -fwd.x);
                }
            }
        }

        float speed = isSliding ? slideSpeed : moveSpeed;
        Vector3 move = (right * inputS + fwd * inputF).normalized * speed;
        rb.linearVelocity = new Vector3(move.x, rb.linearVelocity.y, move.z);
    }

    void SetSlide(bool slide)
    {
        isSliding = slide;
        Vector3 s = transform.localScale;
        s.y = slide ? 0.5f : 1f;
        transform.localScale = s;
        transform.position += Vector3.up * (slide ? -0.5f : 0.5f);
    }

    bool HasHeadroom()
    {
        return !Physics.Raycast(transform.position, Vector3.up, 1.6f);
    }

    // Called by GameManager when the game restarts
    public void ResetState()
    {
        isSliding = false;
        inputF = 0f;
        inputS = 0f;
        Vector3 s = transform.localScale;
        s.y = 1f;
        transform.localScale = s;
    }

    // Read by BugsyAgent for its observations
    public bool IsGroundedNow() { return isGrounded; }
    public bool IsSlidingNow() { return isSliding; }
}