using UnityEngine;

namespace BORis
{
    public static class RepairHintTarget
    {
        public static Transform Resolve(RobotRepair robot, OrderDefinition order, RepairPart held,
            DriveTestStand stand, Transform testButton)
        {
            if (robot == null) return null;
            if (robot.IsTesting) return testButton;
            if (robot.IsOnDriveStand) return robot.ActiveDriveStand.transform;

            var cover = robot.Cover;
            var drive = robot.InstalledDrive;
            bool cleaning = order != null && order.RequiresCleaning && !robot.CheckCleaning(out _);
            bool diagnosis = order != null && order.IndependentDiagnosis;
            bool jointBroken = robot.SolderJoint != null && !robot.SolderJoint.IsRepaired;

            if (held != null)
            {
                if (held.Kind == RepairPartKind.Drive && held.StandResult == DriveTestResult.NotTested
                    && (held.Faulty || diagnosis && held.WasInitiallyInstalled) && stand != null
                    && stand.CanLoad(robot, held, out _)) return stand.Socket;
                if (!held.Faulty && robot.CanPlace(held, true, out _)
                    && (held.Kind != RepairPartKind.Cover || robot.IsFunctional && !cleaning))
                    return held.InstallationAnchor;
                return held.TrayAnchor;
            }

            if (cleaning)
                foreach (var patch in robot.DirtPatches)
                    if (!patch.IsClean && robot.CanClean(patch, out _)) return patch.transform;
            if (robot.TestResult == RobotTestResult.Passed && !cleaning) return null;

            if (cover.IsInstalled)
            {
                bool assembling = robot.IsFunctional && !cleaning && robot.Connector.IsConnected;
                bool initialTest = diagnosis && robot.LastArmObservation == ArmObservation.NotTested
                    && robot.CanRunTest(out _);
                if (initialTest) return testButton;
                var screw = ScrewTarget(robot, cover, assembling);
                if (screw != null) return screw;
                return assembling ? testButton : cover.transform;
            }

            if (drive == null)
            {
                var parts = robot.GetComponentsInChildren<RepairPart>();
                foreach (var part in parts)
                    if (part.Kind == RepairPartKind.Drive && part.WasInitiallyInstalled
                        && part.StandResult == DriveTestResult.NotTested && stand != null
                        && (part.Faulty || diagnosis) && robot.CanMove(part, out _)) return part.transform;
                foreach (var part in parts)
                    if (part.Kind == RepairPartKind.Drive && !part.Faulty && !part.IsInstalled
                        && robot.CanPlace(part, true, out _)) return part.transform;
                return null;
            }

            if (jointBroken)
            {
                if (diagnosis && !robot.JointFaultFound) return robot.SolderJoint.transform;
                return robot.Connector.IsConnected ? robot.Connector.transform : robot.SolderJoint.transform;
            }
            if (drive.Faulty)
            {
                if (diagnosis && !robot.HasInspectedDrive) return drive.transform;
                if (robot.Connector.IsConnected) return robot.Connector.transform;
                return ScrewTarget(robot, drive, false) ?? drive.transform;
            }

            var fastening = ScrewTarget(robot, drive, true);
            if (fastening != null) return fastening;
            if (!robot.Connector.IsConnected) return robot.Connector.transform;
            return robot.CanPlace(cover, true, out _) ? cover.transform : cover.TrayAnchor;
        }

        static Transform ScrewTarget(RobotRepair robot, RepairPart part, bool tightening)
        {
            foreach (var screw in part.Screws)
            {
                if (tightening ? screw.IsTight : screw.IsRemoved) continue;
                if (screw.IsRemoved ? robot.CanInsertScrew(screw, out _) : robot.CanTurn(screw, out _))
                    return screw.transform;
            }
            return null;
        }
    }
}
