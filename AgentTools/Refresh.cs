public static class RefreshAssets { public static string Run(){ UnityEditor.AssetDatabase.Refresh(); return "refreshed"; } }
