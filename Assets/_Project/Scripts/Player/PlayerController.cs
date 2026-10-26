using System.Collections;
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
        Coroutine cameraReturn;
        Vector3 returnPosition;
        Quaternion returnRotation;

        public Camera View => view;
        public bool IsReturning => cameraReturn != null;

        void Awake() => body = GetComponent<CharacterController>();

        public void Tick(Vector2 move, Vector2 look)
        {
            if (IsReturning) return;
            transform.Rotate(0f, look.x * mouseSensitivity, 0f);
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -80f, 80f);
            view.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            verticalSpeed = body.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            Vector3 velocity = (transform.right * move.x + transform.forward * move.y) * speed;
            velocity.y = verticalSpeed;
            body.Move(velocity * Time.deltaTime);
        }

        public void ReturnView(Vector3 position, Quaternion rotation, bool smooth)
        {
            if (cameraReturn != null) StopCoroutine(cameraReturn);
            cameraReturn = null;
            returnPosition = position;
            returnRotation = rotation;
            if (smooth && isActiveAndEnabled) cameraReturn = StartCoroutine(AnimateReturn());
            else FinishReturn();
        }

        IEnumerator AnimateReturn()
        {
            Vector3 startPosition = view.transform.localPosition;
            Quaternion startRotation = view.transform.localRotation;
            for (float elapsed = 0f; elapsed < .45f; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / .45f);
                view.transform.SetLocalPositionAndRotation(Vector3.Lerp(startPosition, returnPosition, t),
                    Quaternion.Slerp(startRotation, returnRotation, t));
                yield return null;
            }
            FinishReturn();
        }

        void FinishReturn()
        {
            if (view != null) view.transform.SetLocalPositionAndRotation(returnPosition, returnRotation);
            cameraReturn = null;
        }

        void OnDisable()
        {
            if (cameraReturn == null) return;
            StopCoroutine(cameraReturn);
            FinishReturn();
        }
    }
}
