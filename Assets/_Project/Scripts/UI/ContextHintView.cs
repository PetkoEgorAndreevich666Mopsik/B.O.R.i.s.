using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ContextHintView : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Text label;
        [SerializeField] UnityEngine.UI.Image progress;
        [SerializeField] RectTransform avoid;
        RectTransform panel, canvas;
        CanvasGroup group;
        Transform target;
        float targetSince;
        bool visible, placeRight;
        string previousText;
        readonly Vector3[] corners = new Vector3[4];

        void Awake()
        {
            panel = (RectTransform)transform;
            canvas = (RectTransform)GetComponentInParent<Canvas>().transform;
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;
        }

        public void Present(string message, Camera view = null, Transform nextTarget = null,
            float amount = -1f, bool immediate = false)
        {
            visible = !string.IsNullOrEmpty(message);
            if (!visible) { Hide(); return; }
            if (target != nextTarget)
            {
                target = nextTarget;
                targetSince = Time.unscaledTime;
                placeRight = true;
                group.alpha = 0f;
            }
            if (message != previousText || progress.gameObject.activeSelf != (amount >= 0f))
            {
                previousText = message;
                label.text = message;
                float width = Mathf.Clamp(label.preferredWidth + 32f, 190f, 380f);
                label.rectTransform.sizeDelta = new Vector2(width - 32f, 100f);
                float height = label.preferredHeight;
                label.rectTransform.sizeDelta = new Vector2(width - 32f, height);
                panel.sizeDelta = new Vector2(width, height + (amount >= 0f ? 38f : 24f));
            }
            progress.gameObject.SetActive(amount >= 0f);
            progress.fillAmount = Mathf.Clamp01(amount);
            Vector2 size = panel.sizeDelta;
            Rect bounds = canvas.rect;
            Vector2 position = new(-size.x * .5f, bounds.yMin + 148f + size.y);
            if (target != null && view != null)
            {
                Vector3 screen = view.WorldToScreenPoint(target.position);
                if (screen.z <= 0f) { Hide(); return; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 point);
                if (Time.unscaledTime - targetSince < .12f)
                    placeRight = point.x + 110f + size.x < bounds.xMax - 16f;
                Vector2 beside = new(placeRight ? point.x + 110f : point.x - size.x - 110f, point.y + 20f);
                beside.y = Mathf.Clamp(beside.y, bounds.yMin + size.y + 140f, bounds.yMax - 16f);
                Rect candidate = new(beside.x, beside.y - size.y, size.x, size.y);
                if (candidate.xMin >= bounds.xMin + 16f && candidate.xMax <= bounds.xMax - 16f
                    && !OverlapsQuest(candidate)) position = beside;
            }
            else if (view == null)
                position = new Vector2(-size.x * .5f, -42f);
            position.x = Mathf.Clamp(position.x, bounds.xMin + 16f, bounds.xMax - size.x - 16f);
            position.y = Mathf.Clamp(position.y, bounds.yMin + size.y + 16f, bounds.yMax - 16f);
            panel.anchoredPosition = position;
            visible = immediate || target == null || Time.unscaledTime - targetSince >= .12f;
        }

        bool OverlapsQuest(Rect candidate)
        {
            if (avoid == null || !avoid.gameObject.activeInHierarchy) return false;
            avoid.GetWorldCorners(corners);
            Vector2 min = canvas.InverseTransformPoint(corners[0]);
            Vector2 max = canvas.InverseTransformPoint(corners[2]);
            return candidate.Overlaps(Rect.MinMaxRect(min.x - 12f, min.y - 12f, max.x + 12f, max.y + 12f));
        }

        public void Hide()
        {
            visible = false;
            target = null;
            targetSince = Time.unscaledTime;
            if (label != null) label.text = "";
            previousText = null;
            if (progress != null) progress.gameObject.SetActive(false);
        }

        void LateUpdate() => group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f,
            Time.unscaledDeltaTime / .1f);
    }
}
