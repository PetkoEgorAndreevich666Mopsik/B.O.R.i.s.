using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] Camera view;
        [SerializeField] float speed = 3f;
        [SerializeField] float mouseSensitivity = 0.12f;
        CharacterController body;
        float pitch;
        float verticalSpeed;

        public Camera View => view;

        void Awake() => body = GetComponent<CharacterController>();

        public void Tick(Vector2 move, Vector2 look)
        {
            transform.Rotate(0f, look.x * mouseSensitivity, 0f);
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -80f, 80f);
            view.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            verticalSpeed = body.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            Vector3 velocity = (transform.right * move.x + transform.forward * move.y) * speed;
            velocity.y = verticalSpeed;
            body.Move(velocity * Time.deltaTime);
        }
    }
}
