using System;
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(IsolatedWorkshopSaves))]

public sealed class IsolatedWorkshopSaves : ITestRunCallback
{
    string previousPath;
    string directory;

    public void RunStarted(ITest testsToRun) { }
    public void RunFinished(ITestResult testResults) { }

    public void TestStarted(ITest test)
    {
        if (test.IsSuite) return;
        previousPath = Environment.GetEnvironmentVariable("BORIS_SAVE_PATH");
        directory = Path.Combine(Path.GetTempPath(), "BORisTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("BORIS_SAVE_PATH", Path.Combine(directory, "workshop.json"));
    }

    public void TestFinished(ITestResult result)
    {
        if (result.Test.IsSuite) return;
        Environment.SetEnvironmentVariable("BORIS_SAVE_PATH", previousPath);
        if (directory == null || !Directory.Exists(directory)) return;
        foreach (string file in new[] { "workshop.json", "workshop.json.tmp", "workshop.json.bak" })
            if (File.Exists(Path.Combine(directory, file))) File.Delete(Path.Combine(directory, file));
        if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
    }
}
