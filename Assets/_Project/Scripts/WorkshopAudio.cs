using System.Collections.Generic;
using UnityEngine;

namespace BORis
{
    public enum WorkshopSound
    {
        UiHover, UiClick, ToolPickUp, PartPickUp, PartInstall, ConnectorOn, ConnectorOff,
        ScrewComplete, CleanComplete, SolderComplete, TestStart, TestFailed, RepairComplete,
        Payment, OrderAccepted, Purchase, RobotPickUp, RobotPlace, DoorOpen, DoorClose,
        StandStart, StandPassed, StandFailed, Hint
    }

    public static class WorkshopAudio
    {
        static readonly Dictionary<WorkshopSound, AudioSource> voices = new();
        static Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { voices.Clear(); root = null; }

        public static void Play(WorkshopSound cue, float volume = .3f, Vector3? position = null)
        {
            if (!Application.isPlaying) return;
            if (!voices.TryGetValue(cue, out var voice) || voice == null)
            {
                var clip = Resources.Load<AudioClip>("WorkshopSounds/" + cue);
                if (clip == null) return;
                if (root == null)
                {
                    root = new GameObject("Workshop audio").transform;
                    Object.DontDestroyOnLoad(root.gameObject);
                }
                voice = new GameObject(cue.ToString()).AddComponent<AudioSource>();
                voice.transform.SetParent(root, false);
                voice.playOnAwake = false;
                voice.clip = clip;
                voice.minDistance = 1.5f;
                voice.maxDistance = 12f;
                voices[cue] = voice;
            }
            if (voice.isPlaying && voice.time < .07f) return;
            voice.spatialBlend = position.HasValue ? .8f : 0f;
            if (position.HasValue) voice.transform.position = position.Value;
            voice.volume = Mathf.Clamp01(volume);
            voice.pitch = cue == WorkshopSound.RepairComplete || cue == WorkshopSound.Payment
                ? 1f : Random.Range(.97f, 1.03f);
            voice.Play();
        }
    }
}
