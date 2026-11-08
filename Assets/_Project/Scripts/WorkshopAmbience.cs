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

        private const float TransitionHalfWidth = 0.85f;
        private const float FadeSpeed = 6f;
        private float outdoorMix;

        private void OnEnable()
        {
            outdoorMix = GetOutdoorMix();
            ApplyMix();
        }

        private void LateUpdate()
        {
            outdoorMix = Mathf.Lerp(outdoorMix, GetOutdoorMix(),
                1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime));
            ApplyMix();
        }

        private float GetOutdoorMix()
        {
            if (listener == null)
                listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
                return 0f;

            float distance = Vector3.Dot(listener.transform.position - transform.position, transform.forward);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-TransitionHalfWidth, TransitionHalfWidth, distance));
        }

        private void ApplyMix()
        {
            ventilation.volume = Mathf.Lerp(.23f, .035f, outdoorMix);
            wind.volume = Mathf.Lerp(.012f, .22f, outdoorMix);
            traffic.volume = Mathf.Lerp(.025f, .14f, outdoorMix);
        }
    }
}
