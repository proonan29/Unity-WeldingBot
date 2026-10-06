using System.Text;
using UnityEngine;
using WeldingBot;

// Step through the planner for one seam and report where it fails.
public static class DiagPlan
{
    public static string Run()
    {
        var job = JobPresets.Get("Box Block");
        var s = WeldSession.Build(job, new RobotGeom(), new GantryLimits(), new WeldParams());
        var c = s.ctx;
        var seam = s.seams.Find(x => x.name == "S11");
        var sb = new StringBuilder();
        int midOk = 0, fixedOk = 0;
        var q = new float[6];
        Vector3 t = seam.Dir;
        Quaternion rot = Quaternion.LookRotation((seam.torchDir + t * Mathf.Tan(10f * Mathf.Deg2Rad)).normalized, t);
        int checks0 = c.collisionChecks, hits0 = c.collisionHits;
        for (float dy = 1.1f; dy <= 2.5f; dy += 0.2f)
        for (float r = 0f; r <= 1.3f; r += 0.25f)
        for (int k = 0; k < 12; k++)
        {
            float a = k * 30f * Mathf.Deg2Rad;
            var b = seam.Mid + new Vector3(r * Mathf.Cos(a), dy, r * Mathf.Sin(a));
            if (!c.lim.Inside(b) || b.y < WeldPlanner.ClearY(c, b)) continue;
            if (!WeldPlanner.SolveFree(c, b, seam.Mid, rot, q, out _)) continue;
            midOk++;
            if (WeldPlanner.TryFixed(c, seam, false, b, null) != null) fixedOk++;
        }
        sb.Append($"midOk={midOk} fixedOk={fixedOk} checks={c.collisionChecks - checks0} hits={c.collisionHits - hits0}\n");
        // the same with collisions ignored: drop all plates
        var saved = new System.Collections.Generic.List<PlateSpec>(c.plates);
        c.plates.Clear(); var savedR = new System.Collections.Generic.List<float>(c.plateRadius); c.plateRadius.Clear();
        int fixedNoCol = 0;
        for (float dy = 1.1f; dy <= 2.5f; dy += 0.2f)
        for (float r = 0f; r <= 1.3f; r += 0.25f)
        for (int k = 0; k < 12; k++)
        {
            float a = k * 30f * Mathf.Deg2Rad;
            var b = seam.Mid + new Vector3(r * Mathf.Cos(a), dy, r * Mathf.Sin(a));
            if (!c.lim.Inside(b) || b.y < WeldPlanner.ClearY(c, b)) continue;
            if (WeldPlanner.TryFixed(c, seam, false, b, null) != null) fixedNoCol++;
        }
        c.plates.AddRange(saved); c.plateRadius.AddRange(savedR);
        sb.Append($"fixedWithoutCollision={fixedNoCol} len={seam.Length:0.00}");
        return sb.ToString();
    }
}
