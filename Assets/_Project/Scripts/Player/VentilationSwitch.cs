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
        public string Hint => IsOn ? "E — выключить вентиляцию" : "E — включить вентиляцию";

        void Start() => UpdateEffects();

        public void Toggle()
        {
            ventilation.mute = !ventilation.mute;
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
