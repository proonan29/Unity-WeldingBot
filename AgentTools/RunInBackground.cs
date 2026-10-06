using UnityEditor;
using UnityEngine;

public static class RunInBackground
{
    // Simulator should keep running when the Editor/Player window is not focused.
    public static string Run()
    {
        PlayerSettings.runInBackground = true;
        Application.runInBackground = true;
        AssetDatabase.SaveAssets();
        return $"runInBackground={PlayerSettings.runInBackground} app={Application.runInBackground}";
    }
}
