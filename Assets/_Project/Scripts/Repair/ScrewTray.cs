using System.Collections.Generic;
using UnityEngine;

namespace BORis
{
    public sealed class ScrewTray : MonoBehaviour
    {
        [SerializeField] BoxCollider floor;
        [SerializeField] AudioClip impactSound;
        readonly List<Rigidbody> screws = new();
        readonly HashSet<Rigidbody> sounded = new();
        AudioSource audioSource;

        public int Count => screws.Count;

        void Awake()
        {
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = .65f;
        }

        public void Collect(ScrewFastener screw)
        {
            if (screw == null) return;
            GameObject visual = screw.CreateLooseVisual(transform);
            Bounds bounds = new(Vector3.zero, Vector3.one * .02f);
            bool hasBounds = false;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                Bounds meshBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = visual.transform.InverseTransformPoint(renderer.transform.TransformPoint(
                        meshBounds.center + Vector3.Scale(meshBounds.extents, sign)));
                    if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(point);
                }
            }
            var collider = visual.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = Vector3.Max(bounds.size, Vector3.one * .01f);
            var body = visual.AddComponent<Rigidbody>();
            body.mass = .012f;
            body.linearDamping = .7f;
            body.angularDamping = 2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxDepenetrationVelocity = 1f;
            body.solverIterations = 10;
            screws.Add(body);
            Drop(body);
        }

        public bool TryTake()
        {
            if (screws.Count == 0) return false;
            int last = screws.Count - 1;
            Rigidbody body = screws[last];
            screws.RemoveAt(last);
            sounded.Remove(body);
            if (body != null)
            {
                body.gameObject.SetActive(false);
                Destroy(body.gameObject);
            }
            return true;
        }

        void Drop(Rigidbody body)
        {
            Bounds bounds = floor != null ? floor.bounds : new Bounds(transform.position, Vector3.zero);
            float angle = screws.IndexOf(body) * 137.5f * Mathf.Deg2Rad;
            float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * .45f;
            Vector3 position = bounds.center + new Vector3(Mathf.Cos(angle) * radius,
                bounds.extents.y + .18f, Mathf.Sin(angle) * radius);
            Quaternion rotation = Quaternion.Euler(70f, angle * Mathf.Rad2Deg, 15f);
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        void FixedUpdate()
        {
            if (floor == null) return;
            Bounds bounds = floor.bounds;
            foreach (Rigidbody body in screws)
            {
                if (body == null) continue;
                Vector3 position = body.position;
                if (position.y < bounds.min.y - .1f || position.x < bounds.min.x - .04f
                    || position.x > bounds.max.x + .04f || position.z < bounds.min.z - .04f
                    || position.z > bounds.max.z + .04f) Drop(body);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            Rigidbody body = collision.rigidbody;
            if (body == null || !screws.Contains(body) || !sounded.Add(body) || impactSound == null) return;
            audioSource.pitch = 1.2f + screws.IndexOf(body) * .07f;
            audioSource.PlayOneShot(impactSound, Mathf.Clamp(collision.relativeVelocity.magnitude * .1f, .1f, .35f));
        }
    }
}
