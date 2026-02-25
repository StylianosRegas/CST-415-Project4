using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    private Rigidbody2D rb;
    public GameObject Attack;

    Vector2 movement;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        
    }

    void Update()
    {
        // Get input from player
        movement.x = Input.GetAxisRaw("Horizontal");
        movement.y = Input.GetAxisRaw("Vertical");
        if (Input.GetKeyDown(KeyCode.E))
        {
            attack();
        }
        
    }
    void FixedUpdate()
    {
        // Apply movement
        rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime);
    }

    void attack()
    {
        Attack.active = true;
    }
}