using UnityEngine;
using WeldingBot;

// Search a compact "folded" park pose: smallest reach below the base (robot +Y = world down).
public static class FindPark
{
    public static string Run()
    {
        var g = new RobotGeom();
        var pts = new Vector3[6];
        float best = 1e9f; float[] bq = null;
        for (float q1 = -150f; q1 <= 40f; q1 += 5f)
        for (float q2 = -160f; q2 <= 160f; q2 += 5f)
        for (float q4 = -120f; q4 <= 120f; q4 += 10f)
        {
            var q = new[] { 0f, q1, q2, 0f, q4, 0f };
            RobotKinematics.Points(g, q, pts);
            float down = float.MinValue, horiz = 0f;
            for (int i = 1; i < 6; i++) { down = Mathf.Max(down, pts[i].y); horiz = Mathf.Max(horiz, new Vector2(pts[i].x, pts[i].z).magnitude); }
            // prefer compact: low reach below base, small horizontal radius, joints away from limits
            float score = down + 0.3f * horiz + 0.002f * RobotKinematics.PostureCost(g, q);
            if (score < best) { best = score; bq = q; }
        }
        RobotKinematics.Points(g, bq, pts);
        return $"park=[{string.Join(",", bq)}] tcpDown={pts[5].y:0.00} wristDown={pts[3].y:0.00} elbowDown={pts[2].y:0.00} tcpR={new Vector2(pts[5].x, pts[5].z).magnitude:0.00}";
    }
}
