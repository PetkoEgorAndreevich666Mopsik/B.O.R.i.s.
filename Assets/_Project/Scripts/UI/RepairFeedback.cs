using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(InteractionController))]
    public sealed class RepairFeedback : MonoBehaviour
    {
        [SerializeField] InteractionRing ring;
        [SerializeField] Image icon;
        [SerializeField] Text result;
        [SerializeField] Sprite hand, screwdriver;
        InteractionController interaction;
        CanvasGroup group;
        RectTransform canvas;
        Transform highlighted, pulseTarget;
        float pulseUntil;
        bool successful;
        ScrewFastener previousScrew;
        RepairPart previousHeld;
        RepairConnector previousConnector;
        bool connectorConnected;
        readonly HashSet<DirtPatch> completedPatches = new();
        string previousFeedback;
        readonly List<Renderer> renderers = new();
        readonly List<MaterialPropertyBlock> originals = new();
        static readonly Color Warm = new(1f,.83f,.53f);
        static readonly Color Good = new(.64f,.94f,.8f);
        static readonly Color Blocked = new(1f,.46f,.34f);

        void Awake()
        {
            interaction = GetComponent<InteractionController>();
            group = ring.GetComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            canvas = ring.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            group.alpha = 0f;
        }

        public void Pulse(Transform target, bool success)
        {
            pulseTarget = target;
            pulseUntil = Time.unscaledTime + .4f;
            successful = success;

        }

        void LateUpdate()
        {
            if (!interaction.IsAtWorkbench || interaction.IsViewMoving)
            { group.alpha = 0f; Highlight(null); previousScrew = null; previousHeld = null; previousConnector = null; return; }
            var driver = interaction.Screwdriver;
            ScrewFastener screw = driver != null ? driver.Target : null;
            if (previousScrew != null && screw == null && (previousScrew.IsRemoved || previousScrew.IsTight)) Pulse(previousScrew.transform, true);
            previousScrew = screw;
            if (interaction.Brush != null && interaction.Brush.Target != null && interaction.Brush.Target.IsClean
                && completedPatches.Add(interaction.Brush.Target)) Pulse(interaction.Brush.Target.transform, true);
            if (previousHeld == null && interaction.HeldPart != null)
            { WorkshopAudio.Play(WorkshopSound.PartPickUp, .16f); }
            previousHeld = interaction.HeldPart;
            var connector = interaction.PointedConnector;
            if (connector != null && previousConnector == connector && connectorConnected != connector.IsConnected) Pulse(connector.transform, true);
            previousConnector = connector;
            connectorConnected = connector != null && connector.IsConnected;
            Transform target = screw != null ? screw.transform : interaction.HeldPart != null ? interaction.HeldPart.transform
                : interaction.PointedScrew != null ? interaction.PointedScrew.transform
                : interaction.PointedConnector != null ? interaction.PointedConnector.transform
                : interaction.PointedPart != null ? interaction.PointedPart.transform : null;
            bool allowed = true;
            if (target != null && interaction.Robot != null && interaction.HeldPart == null && screw == null)
            {
                if (interaction.PointedScrew != null)
                    allowed = interaction.PointedScrew.IsRemoved ? interaction.Robot.CanInsertScrew(interaction.PointedScrew, out _)
                        : interaction.Robot.CanTurn(interaction.PointedScrew, out _);
                else if (interaction.PointedPart != null) allowed = interaction.Robot.CanMove(interaction.PointedPart, out _);
            }
            if (interaction.Feedback != previousFeedback)
            {
                previousFeedback = interaction.Feedback;
                if (!allowed && previousFeedback.Length > 0) Pulse(target, false);
            }
            bool pulse = Time.unscaledTime < pulseUntil && pulseTarget != null;
            bool brushing = interaction.Brush != null && interaction.Brush.IsActive && interaction.Brush.HasContact
                && interaction.Brush.Target != null && !interaction.Brush.Target.IsClean;
            bool soldering = interaction.Soldering != null && interaction.Soldering.IsActive && interaction.Soldering.HasContact;
            bool visible = pulse || screw != null || brushing || soldering
                || interaction.HeldPart != null && interaction.StowProgress > 0f;
            Highlight(!interaction.BrushSelected && !interaction.SolderingSelected && allowed ? target : null);
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 12f);
            if (!visible) return;
            if (pulse) Highlight(null);
            ring.enabled = !pulse;
            ring.color = pulse ? successful ? Good : Blocked : allowed ? Warm : Blocked;
            ring.Progress = pulse ? 1f : screw != null ? driver.ActionProgress
                : brushing ? interaction.Brush.Target.Cleanliness : soldering ? interaction.Soldering.Target.Progress : interaction.StowProgress;
            icon.gameObject.SetActive(false);
            result.gameObject.SetActive(pulse);
            result.text = successful ? "✓" : "×";
            result.color = ring.color;
            Vector2 screen = Mouse.current?.position.ReadValue() ?? Vector2.zero;
            if (pulse || screw != null)
            {
                Vector3 projected = interaction.View.WorldToScreenPoint((pulse ? pulseTarget : target).position);
                if (projected.z <= 0f) { group.alpha = 0f; return; }
                screen = projected;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
            var size = canvas.rect;
            ring.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(local.x + 26f, size.xMin + 32f, size.xMax - 32f),
                Mathf.Clamp(local.y + 18f, size.yMin + 32f, size.yMax - 32f));
        }

        void Highlight(Transform target)
        {
            if (highlighted == target) return;
            for (int i = 0; i < renderers.Count; i++) if (renderers[i] != null) renderers[i].SetPropertyBlock(originals[i]);
            renderers.Clear(); originals.Clear(); highlighted = target;
            if (target == null) return;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                var material = renderer.sharedMaterial;
                if (renderer.GetComponent<DirtPatch>() != null || renderer.GetComponentInParent<ScrewFastener>() != null
                    || material == null || !material.HasProperty("_BaseColor")) continue;
                var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
                var tint = new MaterialPropertyBlock(); renderer.GetPropertyBlock(tint);
                tint.SetColor("_BaseColor", Color.Lerp(material.GetColor("_BaseColor"), Warm, .24f));
                renderers.Add(renderer); originals.Add(original); renderer.SetPropertyBlock(tint);
            }
        }

        void OnDisable() { Highlight(null); if (group != null) group.alpha = 0f; }
    }
}
