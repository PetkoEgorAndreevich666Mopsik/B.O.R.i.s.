using System.Collections;
using UnityEngine;

namespace BORis
{
    public enum ServiceWindowPhase { Loading, Arriving, AwaitingConversation, AwaitingPickup, InRepair, Departing, WaitingForNext,
        ParcelArriving, ParcelReady, ParcelCarried }

    public sealed class ServiceWindow : MonoBehaviour
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] Workbench workbench;
        [SerializeField] Transform shutter;
        [SerializeField] Transform outside;
        [SerializeField] Transform platform;
        [SerializeField] Collider sendButton;
        [SerializeField] Transform[] beltSlats;
        [SerializeField] Transform[] rollers;
        [SerializeField] UnityEngine.UI.Text stateLabel;
        [SerializeField] UnityEngine.UI.Text contractLabel;
        [SerializeField] Canvas speech;
        [SerializeField] UnityEngine.UI.Text speechLabel;
        [SerializeField] Renderer indicator;
        [SerializeField] AudioSource motor;
        [SerializeField] AudioSource signal;
        [SerializeField] float doorTravel = 1.55f;
        [SerializeField] float doorDuration = .85f;
        [SerializeField] float transitDuration = 2.2f;
        Coroutine sequence;
        Vector3 shutterClosed;
        Vector3[] slatOrigins;
        MaterialPropertyBlock lightProperties;
        OrderDefinition displayedOrder;
        Camera view;
        AudioClip bellClip, motorClip;
        float beltOffset, speechUntil, gestureStarted = -10f, nextArrival;
        int dialoguePage;
        string lastReceipt = "";
        EquipmentParcel parcel;
        EquipmentShop terminal;
        UnityEngine.UI.Text buttonLabel;

        public ServiceWindowPhase Phase { get; private set; } = ServiceWindowPhase.Loading;
        public bool IsBusy => sequence != null;
        public bool HasHeardRequest { get; private set; }
        public float DoorOpenness { get; private set; }
        public float BeltDirection { get; private set; }
        public Collider SendButton => sendButton;
        public RobotRepair Robot => session != null ? session.ActiveRobot != null ? session.ActiveRobot : session.VisitorRobot : null;
        public string SpeechText => speechLabel != null ? speechLabel.text : "";
        public string StatusText => stateLabel != null ? stateLabel.text : "";
        public bool IsVisitor(RobotRepair robot) => robot != null && robot == session.VisitorRobot;

        public OrderDefinition DisplayedOrder => displayedOrder;
        public bool IsCarryingParcel => Phase == ServiceWindowPhase.ParcelCarried;
        public bool CanCallVisitor => !IsBusy && (Phase == ServiceWindowPhase.WaitingForNext || Phase == ServiceWindowPhase.Loading)
            && session.EquipmentOfferPending;
        public string ParcelStatus => Phase switch
        {
            ServiceWindowPhase.ParcelArriving => "Коробка прибывает к окну",
            ServiceWindowPhase.ParcelReady => "Заберите коробку с ленты",
            ServiceWindowPhase.ParcelCarried => "Поставьте коробку справа на верстаке",
            _ => Robot != null || IsBusy ? "Доставка после сдачи текущего заказа"
                : terminal != null && terminal.IsOpen ? "Закройте терминал, чтобы получить коробку"
                : "Коробка готовится к выдаче"
        };

        public bool IsParcel(Collider target) => parcel != null && Phase == ServiceWindowPhase.ParcelReady && target == parcel.Hitbox;

        public bool TryTakeParcel(Collider target)
        {
            if (!IsParcel(target) || IsBusy || session.Transport.IsCarrying) return false;
            Phase = ServiceWindowPhase.ParcelCarried;
            parcel.Hitbox.enabled = false;
            return true;
        }

        public void FollowParcel(Camera camera, Ray ray, float reach, int mask)
        {
            parcel.Follow(camera, mask);
            parcel.Preview(ray, reach, mask);
        }

        public bool TryPlaceParcel(Ray ray, float reach, int mask, out string reason)
        {
            reason = "Коробка возвращена на ленту";
            if (!IsCarryingParcel) return false;
            if (!parcel.CanPlace(ray, reach, mask) || !session.TryInstallSolderingStation(out reason))
            {
                CancelParcel();
                return false;
            }
            parcel.Hide();
            signal.Play();
            lastReceipt = "Станция установлена справа на верстаке.\nПайка доступна.";
            Phase = ServiceWindowPhase.WaitingForNext;
            nextArrival = Time.unscaledTime + 1.5f;
            reason = "Станция установлена · пайка доступна";
            return true;
        }

        public void CancelParcel()
        {
            if (!IsCarryingParcel) return;
            parcel.SetOn(platform);
            Phase = ServiceWindowPhase.ParcelReady;
        }

        void Awake()
        {
            terminal = GetComponentInChildren<EquipmentShop>();
            buttonLabel = transform.Find("SendRobotLabel")?.GetComponentInChildren<UnityEngine.UI.Text>();
            shutterClosed = shutter.localPosition;
            slatOrigins = new Vector3[beltSlats.Length];
            for (int i = 0; i < beltSlats.Length; i++) slatOrigins[i] = beltSlats[i].localPosition;
            lightProperties = new MaterialPropertyBlock();
            view = Camera.main;
            speech.gameObject.SetActive(false);
            bellClip = MakeSound(false);
            motorClip = MakeSound(true);
            signal.clip = bellClip;
            motor.clip = motorClip;
            motor.loop = true;
            nextArrival = Time.unscaledTime + .7f;
        }

        void Update()
        {
            if (Phase != ServiceWindowPhase.Departing && session.ActiveOrder != null && Phase != ServiceWindowPhase.InRepair)
            {
                StopSequence();
                Phase = ServiceWindowPhase.InRepair;
                displayedOrder = session.ActiveOrder;
                HasHeardRequest = true;
                SetDoor(0f);
            }
            if (Phase == ServiceWindowPhase.InRepair && session.ActiveOrder == null)
            {
                Phase = ServiceWindowPhase.WaitingForNext;
                nextArrival = Time.unscaledTime + 2f;
            }
            if (!IsBusy && session.ActiveOrder == null && session.IsProgressReady && Robot == null
                && (Phase == ServiceWindowPhase.Loading || Phase == ServiceWindowPhase.WaitingForNext))
            {
                if (session.SolderingStationOrdered)
                {
                    if (terminal != null && !terminal.IsOpen) sequence = StartCoroutine(DeliverParcel());
                }
                else if (!session.EquipmentOfferPending && Time.unscaledTime >= nextArrival)
                    sequence = StartCoroutine(Arrive());
            }

            if ((Phase == ServiceWindowPhase.AwaitingConversation || Phase == ServiceWindowPhase.AwaitingPickup)
                && Robot != null && Time.unscaledTime - gestureStarted <= 1.3f)
                Robot.ShowServiceGesture((Time.unscaledTime - gestureStarted) / 1.3f);
            RefreshDisplay();
        }

        void LateUpdate()
        {
            RobotRepair robot = Robot;
            bool visible = robot != null && view != null && Time.unscaledTime < speechUntil
                && Vector3.Distance(view.transform.position, robot.InspectionPivot.position) < 4.5f;
            speech.gameObject.SetActive(visible);
            if (!visible) return;
            if (Phase == ServiceWindowPhase.AwaitingPickup || Phase == ServiceWindowPhase.Departing || session.Transport.IsAtDelivery)
                robot.FaceServiceVisitor(view.transform.position);
            Bounds bounds = BodyBounds(robot);
            speech.transform.position = new Vector3(bounds.center.x, bounds.max.y + .25f, bounds.center.z);
            speech.transform.rotation = Quaternion.LookRotation(speech.transform.position - view.transform.position);
        }

        public bool Talk(out string reason)
        {
            reason = "";
            if (!session.IsProgressReady) return session.TryReloadProgress(out reason);
            if (IsBusy || Robot == null) { reason = "Дождитесь остановки ленты и закрытия двери"; return false; }
            displayedOrder = session.ActiveOrder != null ? session.ActiveOrder : session.OfferedOrder;
            if (displayedOrder == null) { reason = "Сейчас нет посетителя"; return false; }
            HasHeardRequest = true;
            if (Phase == ServiceWindowPhase.AwaitingConversation) Phase = ServiceWindowPhase.AwaitingPickup;
            string line = dialoguePage++ % 2 == 0 || string.IsNullOrEmpty(displayedOrder.PersonalRemark)
                ? displayedOrder.Symptom : displayedOrder.PersonalRemark;
            Speak(line, 12f);
            gestureStarted = Time.unscaledTime;
            return true;
        }

        public bool TryTakeVisitor(out string reason)
        {
            reason = "";
            if (IsBusy || Phase != ServiceWindowPhase.AwaitingPickup || !HasHeardRequest)
                reason = "Сначала поговорите с роботом: коротко нажмите ЛКМ";
            else if (displayedOrder != session.OfferedOrder || !IsVisitor(Robot))
                reason = "Предложение изменилось. Дождитесь посетителя";
            else if (!Robot.CanRunTest(out reason)) return false;
            if (reason.Length > 0) return false;
            Robot.ResetServicePose();
            if (!session.TryAcceptOrder(out reason)) return false;
            if (!session.Transport.TryPickUp(out reason)) return false;
            Phase = ServiceWindowPhase.InRepair;
            Speak("Подхватывай за корпус. Руку побереги — я ещё на неё рассчитываю.", 5f);
            return true;
        }

        public bool TrySend(out string reason)
        {
            if (CanCallVisitor)
            {
                if (!session.TryContinueWithoutEquipment(out reason)) return false;
                nextArrival = Time.unscaledTime;
                return true;
            }
            reason = "";
            if (IsBusy) reason = "Дождитесь окончания отправки";
            else if (Phase != ServiceWindowPhase.InRepair) reason = "Сначала примите и выполните заказ";
            else if (!session.CanDeliverOrder(out reason)) { Speak(reason, 7f); return false; }
            if (reason.Length > 0) return false;
            displayedOrder = session.ActiveOrder;
            sequence = StartCoroutine(Dispatch());
            return true;
        }

        IEnumerator Arrive()
        {
            Phase = ServiceWindowPhase.Arriving;
            yield return null;
            HasHeardRequest = false;
            dialoguePage = 0;
            speechUntil = 0f;
            if (!session.TryPrepareVisitor(out _))
            {
                Phase = ServiceWindowPhase.Loading;
                nextArrival = Time.unscaledTime + 2f;
                sequence = null;
                yield break;
            }
            displayedOrder = session.OfferedOrder;
            workbench.PositionRobot(outside);
            signal.Play();
            yield return MoveDoor(true);
            yield return Convey(outside, platform);
            yield return MoveDoor(false);
            Phase = ServiceWindowPhase.AwaitingConversation;
            gestureStarted = Time.unscaledTime;
            sequence = null;
            RefreshDisplay();
        }

        IEnumerator Dispatch()
        {
            Phase = ServiceWindowPhase.Departing;
            string instance = session.ActiveOrderId;
            int reward = session.ActiveOrderReward;
            RobotRepair departing = session.ActiveRobot;
            Speak(string.IsNullOrEmpty(displayedOrder.Farewell) ? "Работает! Спасибо за помощь." : displayedOrder.Farewell, 8f);
            for (float elapsed = 0f; elapsed < 1.3f; elapsed += Time.unscaledDeltaTime)
            {
                departing.ShowServiceGesture(elapsed / 1.3f);
                yield return null;
            }
            departing.ShowServiceGesture(0f);
            yield return MoveDoor(true);
            yield return Convey(platform, outside);
            if (session.TryDeliverOrder(instance, out string reason))
            {
                lastReceipt = $"ОПЛАЧЕНО  +{reward} ед.\nБаланс: {session.Balance} ед.\nСпасибо за работу!";
                signal.Play();
                yield return MoveDoor(false);
                Phase = ServiceWindowPhase.WaitingForNext;
                nextArrival = Time.unscaledTime + 3f;
            }
            else
            {
                yield return Convey(outside, platform);
                yield return MoveDoor(false);
                Phase = ServiceWindowPhase.InRepair;
                Speak(reason, 10f);
            }
            sequence = null;
        }

        IEnumerator DeliverParcel()
        {
            Phase = ServiceWindowPhase.ParcelArriving;
            if (parcel == null)
                parcel = new EquipmentParcel(transform, terminal.InstallationPoint);
            parcel.SetOn(outside);
            parcel.Hitbox.enabled = false;
            signal.Play();
            yield return MoveDoor(true);
            yield return Convey(outside, platform, parcel.Root);
            yield return MoveDoor(false);
            parcel.SetOn(platform);
            Phase = ServiceWindowPhase.ParcelReady;
            sequence = null;
        }

        IEnumerator MoveDoor(bool open)
        {
            motor.Play();
            float start = DoorOpenness;
            for (float elapsed = 0f; elapsed < doorDuration; elapsed += Time.unscaledDeltaTime)
            {
                SetDoor(Mathf.Lerp(start, open ? 1f : 0f, Mathf.SmoothStep(0f, 1f, elapsed / doorDuration)));
                yield return null;
            }
            SetDoor(open ? 1f : 0f);
            motor.Stop();
        }

        IEnumerator Convey(Transform from, Transform to, Transform cargo = null)
        {
            bool movingRobot = cargo == null;
            if (movingRobot)
            {
                workbench.PositionRobot(from);
                cargo = Robot.transform;
            }
            Vector3 start = cargo.position;
            Vector3 travel = to.position - from.position;
            BeltDirection = Vector3.Dot(travel, transform.forward) >= 0f ? 1f : -1f;
            motor.Play();
            for (float elapsed = 0f; elapsed < transitDuration; elapsed += Time.unscaledDeltaTime)
            {
                Vector3 position = start + travel * Mathf.SmoothStep(0f, 1f, elapsed / transitDuration);
                MoveBelt(Vector3.Dot(position - cargo.position, transform.forward));
                cargo.position = position;
                if (movingRobot) workbench.SynchronizeRobotPhysics();
                yield return null;
            }
            MoveBelt(Vector3.Dot(start + travel - cargo.position, transform.forward));
            if (movingRobot) workbench.PositionRobot(to);
            else cargo.position = start + travel;
            BeltDirection = 0f;
            motor.Stop();
        }

        void MoveBelt(float distance)
        {
            beltOffset += distance;
            for (int i = 0; i < beltSlats.Length; i++)
            {
                Vector3 p = slatOrigins[i];
                p.z = Mathf.Repeat(p.z + beltOffset + 1f, 2.2f) - 1f;
                beltSlats[i].localPosition = p;
            }
            foreach (Transform roller in rollers) roller.Rotate(Vector3.up, distance / .11f * Mathf.Rad2Deg);
        }

        void SetDoor(float amount)
        {
            DoorOpenness = amount;
            shutter.localPosition = shutterClosed + Vector3.up * (amount * doorTravel);
        }

        void Speak(string line, float duration)
        {
            speechLabel.text = $"<b>{displayedOrder?.ClientName}</b>\n{line}";
            speechUntil = Time.unscaledTime + duration;
        }

        void RefreshDisplay()
        {
            string state, details;
            Color light = new(.24f, .8f, .8f);
            if (!session.IsProgressReady)
            {
                state = "ЗАГРУЗКА ПРИОСТАНОВЛЕНА";
                details = session.SaveStatus + "\nE — повторить загрузку";
                light = new Color(1f, .35f, .12f);
            }
            else if (Phase == ServiceWindowPhase.Arriving || Phase == ServiceWindowPhase.Departing)
            {
                state = Phase == ServiceWindowPhase.Arriving ? "ПРИБЫВАЕТ ПОСЕТИТЕЛЬ" : "СЧАСТЛИВОГО ПУТИ!";
                details = "Дождитесь остановки ленты\nи закрытия двери";
                light = new Color(1f, .6f, .15f);
            }
            else if (Phase == ServiceWindowPhase.ParcelArriving || Phase == ServiceWindowPhase.ParcelReady || IsCarryingParcel)
            {
                state = "СНАБЖЕНИЕ · ОПЛАЧЕНО";
                details = ParcelStatus;
                light = new Color(1f, .65f, .22f);
            }
            else if (Phase == ServiceWindowPhase.WaitingForNext || Phase == ServiceWindowPhase.Loading)
            {
                state = "ОКНО СВОБОДНО";
                details = session.EquipmentOfferPending
                    ? lastReceipt + "\n\nХватает на станцию.\nE на терминале — оборудование.\nКнопка у ленты — следующий посетитель."
                    : lastReceipt + "\n\nE на терминале — заказы и оборудование";
            }
            else
            {
                bool onPlatform = session.ActiveOrder != null && session.Transport.IsAtDelivery && !session.Transport.IsCarrying;
                bool ready = session.CanDeliverOrder(out string reason);
                state = session.ActiveOrder == null ? "ПОСЕТИТЕЛЬ ЖДЁТ" : ready ? "ГОТОВ К ВЫДАЧЕ" : "НА ОБСЛУЖИВАНИИ";
                details = displayedOrder == null ? "" : $"{displayedOrder.ClientName}  /  {displayedOrder.Reward} ед.\n\n"
                    + (HasHeardRequest ? displayedOrder.Symptom + "\n\n" + displayedOrder.Requirements
                        : "Кликните по роботу,\nчтобы выслушать просьбу.");
                if (onPlatform) details = ready ? "Робот собран и проверен.\nНажмите кнопку ОТПРАВИТЬ."
                    : reason + "\n\nЗаберите робота и завершите работу.";
                if (ready) light = new Color(.35f, 1f, .5f);
            }
            if (stateLabel.text != state) stateLabel.text = state;
            if (contractLabel.text != details) contractLabel.text = details;
            if (buttonLabel != null)
            {
                string action = CanCallVisitor ? "ВЫЗВАТЬ" : "ОТПРАВИТЬ";
                if (buttonLabel.text != action) buttonLabel.text = action;
            }
            lightProperties.SetColor("_BaseColor", light);
            lightProperties.SetColor("_EmissionColor", light * 1.5f);
            indicator.SetPropertyBlock(lightProperties);
        }

        static Bounds BodyBounds(RobotRepair robot)
        {
            Bounds bounds = robot.InspectionPivot.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer renderer in robot.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static AudioClip MakeSound(bool loop)
        {
            const int rate = 16000;
            float[] samples = new float[loop ? rate : rate / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                samples[i] = loop ? .09f * Mathf.Sin(2f * Mathf.PI * 80f * t)
                    + .025f * Mathf.Sin(2f * Mathf.PI * 240f * t) * Mathf.Sin(2f * Mathf.PI * 32f * t)
                    : .22f * Mathf.Sin(2f * Mathf.PI * (t < .16f ? 660f : 880f) * t)
                        * Mathf.Exp(-12f * (t < .16f ? t : t - .16f));
            }
            AudioClip clip = AudioClip.Create(loop ? "Window motor" : "Window bell", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void StopSequence()
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            BeltDirection = 0f;
            if (motor != null) motor.Stop();
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            StopSequence();
            parcel?.Hide();
            if (Robot != null)
            {
                Robot.ResetServicePose();
                if (Phase == ServiceWindowPhase.Arriving || Phase == ServiceWindowPhase.Departing)
                    workbench.PositionRobot(platform);
            }
            if (shutter != null) SetDoor(0f);
            if (speech != null) speech.gameObject.SetActive(false);
            Phase = session != null && session.ActiveOrder != null ? ServiceWindowPhase.InRepair : ServiceWindowPhase.Loading;
        }

        void OnDestroy()
        {
            if (bellClip != null) Destroy(bellClip);
            if (motorClip != null) Destroy(motorClip);
            parcel?.Dispose();
        }
    }
}
