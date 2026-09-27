using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class ToonPackageSetup
{
    static AddRequest request; static double deadline;
    public static void Install()
    {
        request = Client.Add("com.unity.toonshader");
        deadline = EditorApplication.timeSinceStartup + 300;
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
        EditorApplication.update -= Poll;
        if (request.IsCompleted && request.Status == StatusCode.Success)
        { Debug.Log("TOON PACKAGE " + request.Result.name + "@" + request.Result.version); EditorApplication.Exit(0); }
        else { Debug.LogError("TOON PACKAGE " + request.Error?.message); EditorApplication.Exit(1); }
    }
}
