using UnityEngine;
using WeldingBot;

// After reopening the saved scene: are all WeldingBot components still attached (no missing scripts)?
public static class CheckScene
{
    public static string Run()
    {
        int missing = 0;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            foreach (var c in go.GetComponents<Component>()) if (c == null) missing++;
        return $"GantryRig={Object.FindObjectsByType<GantryRig>(FindObjectsSortMode.None).Length} " +
               $"RobotArm={Object.FindObjectsByType<RobotArm>(FindObjectsSortMode.None).Length} " +
               $"WeldEffects={Object.FindObjectsByType<WeldEffects>(FindObjectsSortMode.None).Length} " +
               $"FactoryCutaway={Object.FindObjectsByType<FactoryCutaway>(FindObjectsSortMode.None).Length} " +
               $"AppController={Object.FindObjectsByType<AppController>(FindObjectsSortMode.None).Length} " +
               $"UI={Object.FindObjectsByType<WeldingBot.UI.WeldingBotUI>(FindObjectsSortMode.None).Length} missingScripts={missing}";
    }
}
