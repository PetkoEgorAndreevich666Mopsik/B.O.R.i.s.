using UnityEngine;

namespace BORis
{
    public sealed class RepairHUD : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Text hint;
        [SerializeField] UnityEngine.UI.Text repairNotice;
        float noticeUntil;
        bool wasReady;
        RobotTestResult previousTest;
        [SerializeField] UnityEngine.UI.Text assemblyState;
        [SerializeField] UnityEngine.UI.Image cleaningProgress;
        [SerializeField] UnityEngine.UI.Text symptom;
        [SerializeField] UnityEngine.UI.Text controls;
        [SerializeField] GameObject questPanel;
        [SerializeField] ContextHintView context;
        [SerializeField] GameObject toolsPanel;
        [SerializeField] UnityEngine.UI.Image[] toolSlots;
        [SerializeField] UnityEngine.UI.Text cameraHints;
        [SerializeField] UnityEngine.UI.Button collapseButton;
        [SerializeField] GameObject helpPanel;
        [SerializeField] RepairHintView hintHighlight;
        [SerializeField] UnityEngine.UI.Button diagnosisButton;
        [SerializeField] GameObject diagnosisPanel;
        [SerializeField] UnityEngine.UI.Text diagnosisText;
        [SerializeField] UnityEngine.UI.Button diagnosisHelpButton;
        [SerializeField] UnityEngine.UI.Button closeDiagnosisButton;
        CanvasGroup toolsGroup;
        bool showHelp;
        string previousFeedback;
        Transform feedbackTarget;
        RobotRepair previousRobot;
        UnityEngine.UI.Text diagnosisHelpLabel;
        int diagnosisHelpLevel;
        bool showDiagnosis;
        public bool IsHelpOpen => showHelp;

        void Awake()
        {
            if (diagnosisButton != null) diagnosisButton.onClick.AddListener(ToggleDiagnosis);
            if (diagnosisHelpButton != null) diagnosisHelpButton.onClick.AddListener(RevealDiagnosisHelp);
            if (diagnosisHelpButton != null) diagnosisHelpLabel = diagnosisHelpButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (closeDiagnosisButton != null) closeDiagnosisButton.onClick.AddListener(CloseDiagnosisPanel);
            if (toolsPanel != null) toolsGroup = toolsPanel.GetComponent<CanvasGroup>();
        }

        public void Show(InteractionController interaction)
        {
            RobotRepair robot = interaction.Robot;
            if (previousRobot != robot)
            {
                previousRobot = robot;
                wasReady = false;
                previousTest = RobotTestResult.NotTested;
                noticeUntil = 0f;
                diagnosisHelpLevel = 0;
                showHelp = showDiagnosis = false;
                if (cleaningProgress != null) cleaningProgress.fillAmount = CleaningAmount(robot);
            }

            if (controls != null) controls.gameObject.SetActive(false);
            if (toolsPanel != null) toolsPanel.SetActive(true);
            if (UnityEngine.InputSystem.Keyboard.current?.f1Key.wasPressedThisFrame == true)
            { showHelp = !showHelp; showDiagnosis = false; WorkshopAudio.Play(WorkshopSound.Hint, .12f); }
            if (UnityEngine.InputSystem.Keyboard.current?.hKey.wasPressedThisFrame == true) ToggleDiagnosis();
            if (helpPanel != null) helpPanel.SetActive(false);
            if (diagnosisButton != null) diagnosisButton.gameObject.SetActive(false);
            if (hintHighlight != null)
            {
                if (showHelp) hintHighlight.Show(NextHintTarget(interaction), interaction.View);
                else hintHighlight.Hide();
            }
            if (collapseButton != null) collapseButton.gameObject.SetActive(false);
            OrderDefinition order = interaction.ActiveOrder;
            if (diagnosisPanel != null) diagnosisPanel.SetActive(showDiagnosis);
            if (diagnosisText != null && showDiagnosis)
            {
                diagnosisText.text = (robot != null ? robot.Findings : "Нет робота")
                    + (diagnosisHelpLevel > 0 && robot != null ? "\n\n" + robot.DiagnosisHelp(diagnosisHelpLevel) : "");
                float height = Mathf.Max(40f, diagnosisText.preferredHeight);
                diagnosisText.rectTransform.sizeDelta = new Vector2(312f, height);
                if (diagnosisPanel != null) ((RectTransform)diagnosisPanel.transform).sizeDelta = new Vector2(344f, height + 76f);
            }
            if (diagnosisHelpButton != null)
            {
                diagnosisHelpButton.interactable = diagnosisHelpLevel < 3;
                if (diagnosisHelpLabel != null) diagnosisHelpLabel.text = "Подсказка";
            }
            bool cleaning = order != null && order.RequiresCleaning;
            bool clean = !cleaning || robot != null && robot.CheckCleaning(out _);
            bool ready = robot != null && robot.TestResult == RobotTestResult.Passed && clean;
            if (toolsGroup != null) toolsGroup.alpha = robot != null && (robot.IsTesting || robot.IsOnDriveStand) ? .4f : 1f;

            if (symptom != null) symptom.gameObject.SetActive(false);
            if (questPanel != null) questPanel.SetActive(cleaning);
            if (ready && !wasReady || robot != null && robot.TestResult == RobotTestResult.Faulty && previousTest != RobotTestResult.Faulty)
                noticeUntil = Time.unscaledTime + 2.8f;
            wasReady = ready;
            previousTest = robot != null ? robot.TestResult : RobotTestResult.NotTested;
            if (repairNotice != null)
            {
                repairNotice.gameObject.SetActive(robot != null && (robot.IsTesting
                    || Time.unscaledTime < noticeUntil && (ready || robot.TestResult == RobotTestResult.Faulty)));
                repairNotice.text = robot != null && robot.IsTesting ? "Проверка…" : ready ? "Ремонт завершён" : "Рука не прошла тест";
                repairNotice.color = ready ? new Color(.65f, .89f, .8f) : Color.white;
            }
            float cleanliness = CleaningAmount(robot);
            if (assemblyState != null)
            {
                assemblyState.text = $"{Mathf.RoundToInt(cleanliness * 100f)}%";
                assemblyState.color = ready ? new Color(.65f,.89f,.8f) : Color.white;
            }
            if (cleaningProgress != null)
            {
                cleaningProgress.transform.parent.gameObject.SetActive(cleaning);
                cleaningProgress.fillAmount = Mathf.MoveTowards(cleaningProgress.fillAmount, cleanliness, Time.unscaledDeltaTime * 1.2f);
                cleaningProgress.rectTransform.anchorMax = new Vector2(cleaningProgress.fillAmount, 1f);
            }

            if (hint != null)
            {
                string contextText = "";
                Transform target = ContextTarget(interaction);
                if (previousFeedback != interaction.Feedback)
                {
                    previousFeedback = interaction.Feedback;
                    feedbackTarget = target;
                }
                bool feedback = interaction.Feedback.Length > 0 && feedbackTarget == target;
                bool working = robot != null && robot.IsTesting || interaction.HeldPart != null
                    || interaction.Screwdriver != null && interaction.Screwdriver.IsTurning
                    || interaction.Brush != null && interaction.Brush.IsActive
                    || interaction.Soldering != null && interaction.Soldering.IsActive;
                if (feedback && !working && (interaction.Feedback.StartsWith("Сначала") || interaction.Feedback.Contains("недоступ")
                    || interaction.Feedback.StartsWith("Освободите"))) contextText = interaction.Feedback;
                float solderProgress = -1f;
                Transform solderTarget = null;
                var joint = interaction.Soldering != null && interaction.Soldering.IsActive
                    ? interaction.Soldering.Target : interaction.PointedJoint;
                if (joint != null && !joint.IsRepaired && !interaction.BrushSelected && interaction.SolderingAvailable)
                {
                    solderTarget = joint.transform;
                    solderProgress = joint.Progress;
                    contextText = robot.CanSolder(joint, interaction.Session, out string reason)
                        ? interaction.Soldering.IsActive ? $"Пайка  {Mathf.RoundToInt(joint.Progress * 100f)}%" : "Зажмите ЛКМ и ведите по шву"
                        : reason;
                }
                if (context != null)
                {
                    if (interaction.IsViewMoving || showHelp && solderTarget == null || showDiagnosis) context.Hide();
                    else context.Present(contextText, interaction.View, solderTarget,
                        solderProgress >= 0f ? solderProgress
                            : interaction.BrushSelected && interaction.Brush.IsActive && interaction.Brush.Target != null
                                ? interaction.Brush.Target.Cleanliness : -1f,
                        working || feedback);
                }
                else hint.text = contextText;
            }
            if (toolSlots != null)
                for (int i = 0; i < toolSlots.Length; i++)
                    if (toolSlots[i] != null)
                    {
                        toolSlots[i].gameObject.SetActive(i == 2);
                        if (i == 2) toolSlots[i].rectTransform.anchoredPosition = Vector2.zero;
                        var button = toolSlots[i].GetComponent<UnityEngine.UI.Button>();
                        if (button != null) button.interactable = robot == null || !robot.IsTesting && !robot.IsOnDriveStand;
                        bool selected = i == 2 ? interaction.BrushSelected : interaction.SolderingSelected;
                        toolSlots[i].color = selected ? new Color(.24f, .49f, .44f, .96f) : new Color(.055f, .105f, .13f, .94f);
                    }
            if (cameraHints != null) cameraHints.gameObject.SetActive(false);
        }

        public static float CleaningAmount(RobotRepair robot)
        {
            float total = 0f;
            int count = 0;
            if (robot != null)
                foreach (DirtPatch patch in robot.DirtPatches)
                    if (patch != null) { total += patch.Cleanliness; count++; }
            return count == 0 ? 0f : total / count;
        }

        static Transform NextHintTarget(InteractionController interaction)
        {
            var bench = interaction.Robot != null ? interaction.Robot.GetComponentInParent<Workbench>() : null;
            return RepairHintTarget.Resolve(interaction.Robot, interaction.ActiveOrder, interaction.HeldPart,
                interaction.DriveStand, bench != null ? bench.TestButton.transform : null);
        }
        void ToggleDiagnosis() { showDiagnosis = !showDiagnosis; showHelp = false; }
        void RevealDiagnosisHelp() => diagnosisHelpLevel = Mathf.Min(3, diagnosisHelpLevel + 1);
        void CloseDiagnosisPanel() => showDiagnosis = false;
        public bool CloseOverlay()
        {
            if (!showDiagnosis && !showHelp) return false;
            showDiagnosis = false;
            showHelp = false;
            if (hintHighlight != null) hintHighlight.Hide();
            return true;
        }

        static Transform ContextTarget(InteractionController interaction)
        {
            if (interaction.PointedScrew != null) return interaction.PointedScrew.transform;
            if (interaction.PointedJoint != null) return interaction.PointedJoint.transform;
            if (interaction.PointedDirt != null) return interaction.PointedDirt.transform;
            if (interaction.PointedConnector != null) return interaction.PointedConnector.transform;
            if (interaction.PointedPart != null) return interaction.PointedPart.transform;
            return interaction.PointedObject;
        }

        public void ShowShop(string message)
        {
            Hide();
            if (context != null) context.Present(message);
            else if (hint != null) hint.text = message;
        }

        public void Hide()
        {
            if (repairNotice != null) repairNotice.gameObject.SetActive(false);
            if (questPanel != null) questPanel.SetActive(false);
            if (controls != null) controls.gameObject.SetActive(false);
            if (toolsPanel != null) toolsPanel.SetActive(false);
            if (cameraHints != null) cameraHints.gameObject.SetActive(false);
            if (helpPanel != null) helpPanel.SetActive(false);
            if (hintHighlight != null) hintHighlight.Hide();
            if (diagnosisPanel != null) diagnosisPanel.SetActive(false);
            if (diagnosisButton != null) diagnosisButton.gameObject.SetActive(false);
            showDiagnosis = false;
            showHelp = false;
            if (assemblyState != null) assemblyState.text = "";
            if (symptom != null) symptom.text = "";
        }

        void OnDisable()
        {
            Hide();
            if (context != null) context.Hide();
        }

        void OnDestroy()
        {
            if (diagnosisButton != null) diagnosisButton.onClick.RemoveListener(ToggleDiagnosis);
            if (diagnosisHelpButton != null) diagnosisHelpButton.onClick.RemoveListener(RevealDiagnosisHelp);
            if (closeDiagnosisButton != null) closeDiagnosisButton.onClick.RemoveListener(CloseDiagnosisPanel);
        }
    }
}
