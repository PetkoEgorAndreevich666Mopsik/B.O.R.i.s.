using UnityEngine;

namespace BORis
{
    public sealed class RepairHUD : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Text hint;
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
                diagnosisHelpLevel = 0;
                showHelp = showDiagnosis = false;
                if (cleaningProgress != null) cleaningProgress.fillAmount = CleaningAmount(robot);
            }

            if (controls != null) controls.gameObject.SetActive(false);
            if (toolsPanel != null) toolsPanel.SetActive(true);
            if (UnityEngine.InputSystem.Keyboard.current?.f1Key.wasPressedThisFrame == true)
            { showHelp = !showHelp; showDiagnosis = false; }
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
            if (questPanel != null) questPanel.SetActive(cleaning || ready || robot != null && robot.IsTesting);
            float cleanliness = CleaningAmount(robot);
            if (assemblyState != null)
            {
                assemblyState.text = ready ? "✓" : robot != null && robot.IsTesting ? "…" : $"{Mathf.RoundToInt(cleanliness * 100f)}%";
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
                if (context != null)
                {
                    if (interaction.IsViewMoving || showHelp || showDiagnosis) context.Hide();
                    else context.Present(contextText, interaction.View, null,
                        interaction.SolderingSelected && interaction.Soldering.IsActive ? interaction.Soldering.Target.Progress
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
            RobotRepair robot = interaction.Robot;
            if (robot == null || robot.IsTesting) return null;
            if (interaction.HeldPart != null) return interaction.DropAnchor != null ? interaction.DropAnchor : interaction.HeldPart.TrayAnchor;
            if (robot.IsOnDriveStand) return interaction.DriveStand.transform;
            bool cleaning = interaction.ActiveOrder != null && interaction.ActiveOrder.RequiresCleaning && !robot.CheckCleaning(out _);
            if (cleaning)
                foreach (var patch in robot.DirtPatches)
                    if (!patch.IsClean && robot.CanClean(patch, out _)) return patch.transform;
            if (robot.TestResult == RobotTestResult.Passed && !cleaning) return null;
            var cover = robot.Cover;
            var drive = robot.InstalledDrive;
            if (cover.IsInstalled)
            {
                bool assembling = robot.IsFunctional && !cleaning && robot.Connector.IsConnected;
                foreach (var screw in cover.Screws)
                    if (assembling ? !screw.IsTight : !screw.IsRemoved) return screw.transform;
                return assembling ? interaction.Robot.GetComponentInParent<Workbench>().TestButton.transform : cover.transform;
            }
            if (drive == null)
            {
                foreach (var part in robot.GetComponentsInChildren<RepairPart>())
                    if (part.Kind == RepairPartKind.Drive && part.Faulty && part.StandResult == DriveTestResult.NotTested
                        && interaction.DriveStand != null) return interaction.DriveStand.transform;
                foreach (var part in robot.GetComponentsInChildren<RepairPart>())
                    if (part.Kind == RepairPartKind.Drive && !part.Faulty && !part.IsInstalled) return part.transform;
                return null;
            }
            if (drive.Faulty)
            {
                if (robot.Connector.IsConnected) return robot.Connector.transform;
                foreach (var screw in drive.Screws) if (!screw.IsRemoved) return screw.transform;
                return drive.transform;
            }
            if (robot.SolderJoint != null && !robot.SolderJoint.IsRepaired)
                return robot.Connector.IsConnected ? robot.Connector.transform : robot.SolderJoint.transform;
            foreach (var screw in drive.Screws) if (!screw.IsTight) return screw.transform;
            return robot.Connector.IsConnected ? cover.transform : robot.Connector.transform;
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
