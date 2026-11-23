using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
static class CodexRunBrushScrewTests
{
    const string SessionKey = "Codex.BrushScrewTests.20261123.v3";
    static TestRunnerApi api;

    static CodexRunBrushScrewTests() => EditorApplication.delayCall += RunOnce;

    static void RunOnce()
    {
        if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(SessionKey, true);
        api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks());
        Debug.Log("CODEX_BRUSH_SCREW_TESTS_STARTED");
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode,
            testNames = new[]
            {
                "ScrewdriverFlowTests.ScrewColliderIsNotOccludedByRobotProxyColliders",
                "ScrewdriverFlowTests.ScrewWorksWithoutSelectingToolAndBrushTogglesOff",
                "BrushFlowTests.RealMouseSelectsBrushAndLocallyCleans_StationaryHoldDoesNothing"
            }
        }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result) =>
            Debug.Log($"CODEX_BRUSH_SCREW_TESTS_FINISHED {result.TestStatus} pass={result.PassCount} fail={result.FailCount}");

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) =>
            Debug.Log($"CODEX_TEST_RESULT {result.FullName} {result.TestStatus} {result.Message}");
    }
}
