using UnityEngine;

namespace BORis
{
    [DisallowMultipleComponent]
    public sealed class VentilationSwitch : MonoBehaviour
    {
        [SerializeField] AudioSource ventilation;
        [SerializeField] ParticleSystem intakeParticles;
        [SerializeField] Renderer indicator;

        public bool IsOn => !ventilation.mute;

        void Start() => UpdateEffects();

        public void Toggle()
        {
            ventilation.mute = !ventilation.mute;
            WorkshopAudio.Play(WorkshopSound.ToolPickUp, .18f, transform.position);
            UpdateEffects();
        }

        void UpdateEffects()
        {
            indicator.enabled = IsOn;
            if (IsOn)
                intakeParticles.Play();
            else
                intakeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
