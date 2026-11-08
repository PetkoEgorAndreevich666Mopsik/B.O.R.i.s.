using UnityEngine;

namespace BORis
{
    [DisallowMultipleComponent]
    public sealed class WorkshopAmbience : MonoBehaviour
    {
        [SerializeField] private AudioListener listener;
        [SerializeField] private AudioSource ventilation;
        [SerializeField] private AudioSource wind;
        [SerializeField] private AudioSource traffic;
        [SerializeField] private WorkshopDoor balconyDoor;

        private const float TransitionHalfWidth = 0.85f;
        private const float FadeSpeed = 6f;
        private float outdoorMix;

        private void OnEnable()
        {
            float distance = DistanceToThreshold();
            outdoorMix = GetOutdoorMix(distance);
            ApplyMix(distance);
        }

        private void LateUpdate()
        {
            float distance = DistanceToThreshold();
            outdoorMix = Mathf.Lerp(outdoorMix, GetOutdoorMix(distance),
                1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime));
            ApplyMix(distance);
        }

        private float DistanceToThreshold()
        {
            if (listener == null)
                listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
                return -TransitionHalfWidth;

            return Vector3.Dot(listener.transform.position - transform.position, transform.forward);
        }

        private static float GetOutdoorMix(float distance) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-TransitionHalfWidth, TransitionHalfWidth, distance));

        private void ApplyMix(float distance)
        {
            bool outside = distance > 0f;
            float aperture = balconyDoor != null ? balconyDoor.OpenFraction : 1f;
            float mix = Mathf.Lerp(outside ? 1f : 0f, outdoorMix, aperture);
            float streetGain = outside ? 1f : aperture;
            ventilation.volume = Mathf.Lerp(.025f, .004f, mix);
            wind.volume = Mathf.Lerp(.012f, .22f, mix) * streetGain;
            traffic.volume = Mathf.Lerp(.025f, .14f, mix) * streetGain;
        }
    }
}
