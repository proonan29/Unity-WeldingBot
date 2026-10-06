// Play mode: switch the UI language. Run: wb.ps1 run_script --file AgentTools/SetLang.cs --entry SetLangEn.Run (or SetLangKo.Run)
public static class SetLangEn { public static string Run() { WeldingBot.UI.Loc.Korean = false; return "lang=en"; } }
public static class SetLangKo { public static string Run() { WeldingBot.UI.Loc.Korean = true; return "lang=ko"; } }
