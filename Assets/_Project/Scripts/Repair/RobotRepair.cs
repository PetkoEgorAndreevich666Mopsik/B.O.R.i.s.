using UnityEngine;

namespace BORis
{
    public sealed class RobotRepair : MonoBehaviour
    {
        [SerializeField] RepairPart cover;
        [SerializeField] RepairPart[] drives = System.Array.Empty<RepairPart>();
        [SerializeField] RepairConnector connector;

        public RepairPart Cover => cover;
        public RepairConnector Connector => connector;
        public RepairPart InstalledDrive
        {
            get
            {
                foreach (RepairPart drive in drives)
                    if (drive != null && drive.IsInstalled) return drive;
                return null;
            }
        }

        public bool CanTurn(ScrewFastener screw, out string reason)
        {
            reason = "";
            if (screw == null || !screw.isActiveAndEnabled || !isActiveAndEnabled || screw.GetComponentInParent<RobotRepair>() != this)
                reason = "Винт недоступен";
            else
            {
                RepairPart part = screw.GetComponentInParent<RepairPart>();
                if (part != null && !part.IsInstalled) reason = "Сначала установите деталь в робота";
                else if (part != null && part.Kind == RepairPartKind.Drive && cover != null && cover.IsInstalled)
                    reason = "Сначала снимите крышку";
            }
            return reason.Length == 0;
        }

        public float Turn(ScrewFastener screw, float clockwiseDegrees)
        {
            if (!CanTurn(screw, out _)) return 0f;
            return screw.Turn(clockwiseDegrees);
        }

        public bool CanClean(DirtPatch patch, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || patch == null || !patch.isActiveAndEnabled || patch.GetComponentInParent<RobotRepair>() != this)
                reason = "Поверхность недоступна";
            else if (patch.RequiresOpenCover && cover != null && cover.IsInstalled)
                reason = "Сначала снимите крышку";
            return reason.Length == 0;
        }

        bool Owns(RepairPart part) => part != null && part.isActiveAndEnabled && isActiveAndEnabled
            && part.GetComponentInParent<RobotRepair>() == this
            && (part == cover || System.Array.IndexOf(drives, part) >= 0)
            && part.InstallationAnchor != null && part.TrayAnchor != null;

        public bool CanMove(RepairPart part, out string reason)
        {
            reason = "";
            if (!Owns(part)) reason = "Деталь недоступна";
            else if (part.Kind == RepairPartKind.Drive && cover.IsInstalled) reason = "Сначала снимите крышку";
            else if (part.Kind == RepairPartKind.Drive && connector.IsConnected) reason = "Сначала отсоедините разъём";
            else if (part.IsInstalled)
            {
                int remaining = 0;
                foreach (ScrewFastener screw in part.Screws) if (screw.Tightness > .0001f) remaining++;
                if (remaining > 0) reason = $"Освободите винты: осталось {remaining}";
            }
            return reason.Length == 0;
        }

        public bool CanPlace(RepairPart part, bool inRobot, out string reason)
        {
            if (!CanMove(part, out reason)) return false;
            if (inRobot)
            {
                if (part.Kind == RepairPartKind.Drive && InstalledDrive != null && InstalledDrive != part)
                    reason = "Место занято: сначала уберите установленный привод в лоток";
                else if (part.Kind == RepairPartKind.Cover && InstalledDrive == null)
                    reason = "Сначала установите привод";
                else if (part.Kind == RepairPartKind.Cover && !FullyTightened(InstalledDrive))
                    reason = "Сначала затяните оба винта привода";
                else if (part.Kind == RepairPartKind.Cover && !connector.IsConnected)
                    reason = "Сначала подключите разъём";
            }
            return reason.Length == 0;
        }

        public bool TryPlace(RepairPart part, bool inRobot, out string reason)
        {
            if (!CanPlace(part, inRobot, out reason)) return false;
            part.Place(inRobot);
            return true;
        }

        public bool ToggleConnection(RepairConnector target, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || target == null || target != connector || !target.isActiveAndEnabled)
                reason = "Разъём недоступен";
            else if (cover.IsInstalled) reason = "Сначала снимите крышку";
            else if (InstalledDrive == null) reason = "Сначала установите привод";
            else if (!connector.IsConnected && !FullyTightened(InstalledDrive)) reason = "Сначала затяните оба винта привода";
            if (reason.Length > 0) return false;
            connector.SetConnected(!connector.IsConnected);
            return true;
        }

        public static bool FullyTightened(RepairPart part)
        {
            if (part == null) return false;
            foreach (ScrewFastener screw in part.Screws) if (screw.Tightness < .9999f) return false;
            return true;
        }

        public string AssemblyHint
        {
            get
            {
                RepairPart drive = InstalledDrive;
                if (cover.IsInstalled)
                    return drive != null && !drive.Faulty
                        ? FullyTightened(cover) ? "Сборка завершена: исправный привод установлен" : "Затяните четыре винта крышки"
                        : "Замените неисправный привод: освободите четыре винта и снимите крышку";
                if (drive == null) return "Перенесите исправный привод из зелёного лотка в робота";
                if (drive.Faulty) return connector.IsConnected ? "Рукой отсоедините разъём справа от привода"
                    : "Освободите оба винта привода и перенесите его в красный лоток";
                if (!FullyTightened(drive)) return "Затяните оба винта исправного привода";
                return connector.IsConnected ? "Верните крышку из левого лотка на робота" : "Рукой подключите разъём";
            }
        }
    }
}
