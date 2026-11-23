using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BORis
{
    public enum WorkbenchPointerMode { Hidden, Dot, Hand }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkbenchPointer : MaskableGraphic
    {
        [SerializeField] Image hand;
        InteractionController interaction;
        RectTransform canvas;
        public WorkbenchPointerMode Mode { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        void LateUpdate()
        {
            if (interaction == null) interaction = GetComponentInParent<InteractionController>();
            Mode = WorkbenchPointerMode.Hidden;
            if (interaction != null && interaction.IsAtWorkbench)
            {
                bool overUI = interaction.IsPointerOverUI;
                bool tool = interaction.BrushSelected || interaction.SolderingSelected
                    || interaction.Screwdriver != null && interaction.Screwdriver.IsTurning;
                if (overUI || !tool && !interaction.IsViewMoving)
                    Mode = !overUI && CanAct() ? WorkbenchPointerMode.Hand : WorkbenchPointerMode.Dot;
                Cursor.visible = false;
            }
            canvasRenderer.SetAlpha(Mode == WorkbenchPointerMode.Dot ? 1f : 0f);
            if (hand != null) hand.enabled = Mode == WorkbenchPointerMode.Hand;
            if (Mode == WorkbenchPointerMode.Hidden) return;
            if (canvas == null) canvas = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,
                Mouse.current?.position.ReadValue() ?? Vector2.zero, null, out Vector2 point);
            rectTransform.anchoredPosition = point;
            if (hand != null) hand.rectTransform.anchoredPosition = point;
            transform.SetAsLastSibling();
            if (hand != null) hand.transform.SetAsLastSibling();
        }

        bool CanAct()
        {
            var robot = interaction.Robot;
            if (robot == null || robot.IsTesting) return false;
            if (interaction.HeldPart != null) return true;
            if (interaction.PointedStand != null && interaction.PointedStand.Part != null)
                return !interaction.PointedStand.IsTesting;
            if (interaction.PointedScrew != null)
                return interaction.PointedScrew.IsRemoved ? robot.CanInsertScrew(interaction.PointedScrew, out _)
                    : robot.CanTurn(interaction.PointedScrew, out _);
            if (interaction.PointedConnector != null) return robot.CanToggleConnection(interaction.PointedConnector, out _);
            if (interaction.PointedJoint != null)
                return interaction.SolderingAvailable && robot.CanSolder(interaction.PointedJoint, interaction.Session, out _);
            if (interaction.PointedTestButton) return robot.CanRunTest(out _);
            return interaction.PointedPart != null && robot.CanMove(interaction.PointedPart, out _);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f, b = (i + 1) * Mathf.PI * 2f / 24f;
                int start = mesh.currentVertCount;
                mesh.AddVert(Vector3.zero, color, Vector2.zero);
                mesh.AddVert(new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * 2.5f, color, Vector2.zero);
                mesh.AddVert(new Vector3(Mathf.Cos(b), Mathf.Sin(b)) * 2.5f, color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
            }
        }
    }
}
