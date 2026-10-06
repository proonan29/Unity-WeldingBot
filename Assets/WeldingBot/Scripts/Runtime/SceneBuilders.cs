using UnityEngine;
using UnityEngine.Rendering;

namespace WeldingBot
{
    /// <summary>Factory hall: floor, platen, walls with columns, pitched roof, overhead crane runway, floor rails.</summary>
    public static class FactoryBuilder
    {
        public const float HallX = 27f, HallZ = 14f, Eave = 12f, Ridge = 15f;

        public static Transform Build(Transform parent, IMaterialProvider mats)
        {
            var root = Prims.Node("Factory", parent, Vector3.zero);
            var concrete = mats.Get("Concrete", new Color(0.55f, 0.55f, 0.53f), 0.15f);
            var wall = mats.Get("WallPanel", new Color(0.70f, 0.73f, 0.76f), 0.3f, 0.3f);
            var wallLow = mats.Get("WallLow", new Color(0.42f, 0.46f, 0.50f), 0.3f, 0.3f);
            var steel = mats.Get("SteelBlue", new Color(0.20f, 0.33f, 0.50f), 0.4f, 0.6f);
            var roof = mats.Get("Roof", new Color(0.60f, 0.62f, 0.64f), 0.3f, 0.4f);
            var sky = mats.Get("Skylight", new Color(0.85f, 0.92f, 1.0f), 0.8f);
            var platen = mats.Get("Platen", new Color(0.23f, 0.24f, 0.25f), 0.5f, 0.8f);
            var rail = mats.Get("Rail", new Color(0.35f, 0.35f, 0.36f), 0.6f, 0.9f);
            var yellow = mats.Get("OverheadCraneRed", new Color(0.70f, 0.16f, 0.12f), 0.4f, 0.3f);
            var stripe = mats.Get("SafetyStripe", new Color(0.95f, 0.80f, 0.15f), 0.2f);

            Prims.Box("Floor", root, new Vector3(0f, -0.05f, 0f), new Vector3(HallX * 2f, 0.1f, HallZ * 2f), concrete);
            // welding platen (steel table) where the plates are fitted
            var pl = Prims.Box("Platen", root, new Vector3(0f, JobBuilder.PlatenTop * 0.5f, 0f), new Vector3(44f, JobBuilder.PlatenTop, 12.4f), platen);
            for (int i = -10; i <= 10; i++)
                Prims.Box($"PlatenSlot{i}", root, new Vector3(i * 2.1f, JobBuilder.PlatenTop + 0.001f, 0f), new Vector3(0.04f, 0.002f, 12.4f), rail, null, false);
            // safety lines and gantry floor rails
            foreach (var z in new[] { -6.6f, 6.6f })
                Prims.Box("SafetyLine", root, new Vector3(0f, 0.002f, z), new Vector3(46f, 0.004f, 0.12f), stripe, null, false);
            foreach (var z in new[] { -9f, 9f })
            {
                Prims.Box("GantryRail", root, new Vector3(0f, 0.08f, z), new Vector3(50f, 0.16f, 0.14f), rail);
                Prims.Box("RailBed", root, new Vector3(0f, 0.01f, z), new Vector3(50f, 0.02f, 0.8f), platen);
            }

            // walls: lower dark band + upper cladding (one node per side so the camera can cut them away)
            var walls = Prims.Node("Walls", root, Vector3.zero);
            var cut = root.gameObject.AddComponent<FactoryCutaway>();
            Transform side(string n) => Prims.Node(n, walls, Vector3.zero);
            var wn = side("North"); var ws = side("South"); var we = side("East"); var ww = side("West");
            void Wall(Transform w, string n, Vector3 c, Vector3 s)
            {
                Prims.Box(n + "_Low", w, new Vector3(c.x, 1.25f, c.z), new Vector3(s.x, 2.5f, s.z), wallLow);
                Prims.Box(n + "_High", w, new Vector3(c.x, 2.5f + (Eave - 2.5f) * 0.5f, c.z), new Vector3(s.x, Eave - 2.5f, s.z), wall);
            }
            Wall(wn, "WallNorth", new Vector3(0f, 0f, HallZ), new Vector3(HallX * 2f, 0f, 0.25f));
            Wall(ws, "WallSouth", new Vector3(0f, 0f, -HallZ), new Vector3(HallX * 2f, 0f, 0.25f));
            Wall(we, "WallEast", new Vector3(HallX, 0f, 0f), new Vector3(0.25f, 0f, HallZ * 2f));
            // west wall with a big door opening (z -6..6)
            Wall(ww, "WallWestA", new Vector3(-HallX, 0f, -10f), new Vector3(0.25f, 0f, 8f));
            Wall(ww, "WallWestB", new Vector3(-HallX, 0f, 10f), new Vector3(0.25f, 0f, 8f));
            Prims.Box("WallWestLintel", ww, new Vector3(-HallX, 9.5f, 0f), new Vector3(0.25f, 5f, 12f), wall);
            Prims.Box("DoorShutter", ww, new Vector3(-HallX - 0.05f, 3.5f, 0f), new Vector3(0.1f, 7f, 12f), wallLow);
            cut.north = wn.gameObject; cut.south = ws.gameObject; cut.east = we.gameObject; cut.west = ww.gameObject;

            // columns, brackets and crane runway belong to their wall side (cut away together)
            for (float x = -HallX; x <= HallX + 0.01f; x += 6f)
            foreach (var z in new[] { -HallZ + 0.45f, HallZ - 0.45f })
            {
                var parentSide = z < 0f ? ws : wn;
                Prims.Box($"Col_{x}_{z}", parentSide, new Vector3(x, Eave * 0.5f, z), new Vector3(0.45f, Eave, 0.6f), steel);
                Prims.Box($"Bracket_{x}_{z}", parentSide, new Vector3(x, 10.0f, z - Mathf.Sign(z) * 0.55f), new Vector3(0.4f, 0.5f, 0.7f), steel);
            }
            foreach (var z in new[] { -HallZ + 1.1f, HallZ - 1.1f })
                Prims.Box("RunwayBeam", z < 0f ? ws : wn, new Vector3(0f, 10.5f, z), new Vector3(HallX * 2f, 0.6f, 0.4f), steel);
            var crane = Prims.Node("OverheadCrane", root, new Vector3(23.5f, 0f, 0f));
            foreach (var dx in new[] { -0.7f, 0.7f })
                Prims.Box("CraneGirder", crane, new Vector3(dx, 11.2f, 0f), new Vector3(0.45f, 0.9f, HallZ * 2f - 2.4f), yellow);
            Prims.Box("CraneHoist", crane, new Vector3(0f, 11.85f, 3f), new Vector3(2.0f, 0.6f, 1.6f), yellow);
            Prims.Box("CraneHook", crane, new Vector3(0f, 9.8f, 3f), new Vector3(0.3f, 0.5f, 0.3f), steel);

            // pitched roof (no shadows so the sun lights the hall) with trusses and skylight strips
            var rf = Prims.Node("Roof", root, Vector3.zero);
            float slope = Mathf.Atan2(Ridge - Eave, HallZ) * Mathf.Rad2Deg;
            float run = Mathf.Sqrt(HallZ * HallZ + (Ridge - Eave) * (Ridge - Eave));
            foreach (var s in new[] { -1f, 1f })
            {
                var p = Prims.Box("RoofPanel", rf, new Vector3(0f, (Eave + Ridge) * 0.5f, s * HallZ * 0.5f),
                    new Vector3(HallX * 2f + 0.6f, 0.12f, run + 0.3f), roof, Quaternion.Euler(s * slope, 0f, 0f), false);
                var sk = Prims.Box("Skylight", rf, new Vector3(0f, (Eave + Ridge) * 0.5f - 0.07f, s * HallZ * 0.5f),
                    new Vector3(HallX * 2f - 2f, 0.02f, 1.6f), sky, Quaternion.Euler(s * slope, 0f, 0f), false);
            }
            for (float x = -HallX + 6f; x < HallX; x += 6f)
            {
                foreach (var s in new[] { -1f, 1f })
                    Prims.Box("Rafter", rf, new Vector3(x, (Eave + Ridge) * 0.5f - 0.35f, s * HallZ * 0.5f),
                        new Vector3(0.25f, 0.5f, run), steel, Quaternion.Euler(s * slope, 0f, 0f), false);
                Prims.Box("Tie", rf, new Vector3(x, Eave, 0f), new Vector3(0.18f, 0.18f, HallZ * 2f), steel, null, false);
            }
            foreach (var r in rf.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            cut.roof = rf.gameObject;
            return root;
        }
    }

    /// <summary>Hides the roof / walls that stand between an outside camera and the hall interior.</summary>
    public class FactoryCutaway : MonoBehaviour
    {
        public GameObject roof, north, south, east, west;

        public void UpdateFor(Vector3 cam)
        {
            Set(roof, cam.y < FactoryBuilder.Eave - 0.3f);
            Set(north, cam.z < FactoryBuilder.HallZ);
            Set(south, cam.z > -FactoryBuilder.HallZ);
            Set(east, cam.x < FactoryBuilder.HallX);
            Set(west, cam.x > -FactoryBuilder.HallX);
        }

        static void Set(GameObject g, bool visible)
        {
            if (g == null) return;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                if (r.enabled != visible) r.enabled = visible;
        }
    }

    public static class GantryBuilder
    {
        public const float RailZ = 9f, GirderY = 8.3f;

        public static GantryRig Build(Transform parent, IMaterialProvider mats)
        {
            var yellow = mats.Get("GantryYellow", new Color(0.98f, 0.70f, 0.08f), 0.45f, 0.3f);
            var dark = mats.Get("GantryDark", new Color(0.18f, 0.19f, 0.21f), 0.4f, 0.6f);
            var black = mats.Get("Rubber", new Color(0.08f, 0.08f, 0.08f), 0.2f);

            var root = Prims.Node("WeldingGantry", parent, Vector3.zero);
            var rig = root.gameObject.AddComponent<GantryRig>();
            var bridge = Prims.Node("Bridge", root, Vector3.zero);
            foreach (var z in new[] { -RailZ, RailZ })
            {
                // end carriage on the rail + A-frame legs
                Prims.Box("EndCarriage", bridge, new Vector3(0f, 0.45f, z), new Vector3(3.2f, 0.5f, 0.6f), yellow);
                foreach (var x in new[] { -1.3f, 1.3f })
                    Prims.Cyl("Wheel", bridge, new Vector3(x, 0.25f, z), 0.22f, 0.3f, black, Quaternion.Euler(90f, 0f, 0f));
                foreach (var x in new[] { -1.0f, 1.0f })
                    Prims.Box("Leg", bridge, new Vector3(x * 0.7f, 4.4f, z), new Vector3(0.45f, 7.8f, 0.55f), yellow,
                        Quaternion.Euler(0f, 0f, -x * 4.5f));
                Prims.Box("LegHead", bridge, new Vector3(0f, 8.1f, z), new Vector3(1.9f, 0.5f, 0.8f), yellow);
                Prims.Box("Cabinet", bridge, new Vector3(0f, 1.6f, z + Mathf.Sign(z) * 0.55f), new Vector3(0.9f, 1.4f, 0.4f), dark);
            }
            foreach (var x in new[] { -0.6f, 0.6f })
            {
                Prims.Box("Girder", bridge, new Vector3(x, GirderY, 0f), new Vector3(0.35f, 0.9f, RailZ * 2f + 0.8f), yellow);
                Prims.Box("TrolleyRail", bridge, new Vector3(x, GirderY + 0.47f, 0f), new Vector3(0.08f, 0.05f, RailZ * 2f), dark);
            }
            foreach (var z in new[] { -RailZ, RailZ })
                Prims.Box("CrossTie", bridge, new Vector3(0f, GirderY, z), new Vector3(1.6f, 0.6f, 0.4f), yellow);
            // cable tray along the girder
            Prims.Box("CableTray", bridge, new Vector3(0.9f, GirderY + 0.3f, 0f), new Vector3(0.25f, 0.08f, RailZ * 2f), dark);

            var trolley = Prims.Node("Trolley", bridge, new Vector3(0f, rig.trolleyY, 0f));
            Prims.Box("TrolleyFrame", trolley, new Vector3(0f, 0f, 0f), new Vector3(1.75f, 0.5f, 1.3f), yellow);
            Prims.Box("Drive", trolley, new Vector3(0.55f, 0.45f, 0.35f), new Vector3(0.4f, 0.4f, 0.4f), dark);
            Prims.Box("WireDrum", trolley, new Vector3(-0.5f, 0.45f, -0.3f), new Vector3(0.5f, 0.5f, 0.5f), dark);
            float sleeveTop = rig.trolleyY + 0.3f, sleeveBottom = rig.sleeveBottomY;
            Prims.Box("MastSleeve", trolley, new Vector3(0f, (sleeveTop + sleeveBottom) * 0.5f - rig.trolleyY, 0f),
                new Vector3(0.48f, sleeveTop - sleeveBottom, 0.48f), yellow);
            var inner = Prims.Box("MastColumn", trolley, Vector3.zero, Vector3.one, dark);
            var mount = Prims.Node("RobotMount", trolley, new Vector3(0f, -3f, 0f));
            Prims.Box("MountPlate", mount, new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.08f, 0.7f), dark);

            rig.bridge = bridge; rig.trolley = trolley; rig.mount = mount; rig.mastInner = inner.transform;
            return rig;
        }
    }

    public static class RobotBuilder
    {
        public static RobotArm Build(Transform mount, IMaterialProvider mats, RobotGeom g)
        {
            var orange = mats.Get("RobotOrange", new Color(1.0f, 0.42f, 0.05f), 0.55f, 0.1f);
            var gray = mats.Get("RobotGray", new Color(0.22f, 0.23f, 0.25f), 0.4f, 0.5f);
            var copper = mats.Get("Copper", new Color(0.85f, 0.50f, 0.25f), 0.7f, 0.9f);
            var torchMat = mats.Get("TorchBlack", new Color(0.10f, 0.10f, 0.11f), 0.5f, 0.2f);
            var cable = mats.Get("Cable", new Color(0.05f, 0.05f, 0.06f), 0.3f);

            // base flange of the inverted robot: robot +Y points to the floor
            var baseT = Prims.Node("RobotBase", mount, Vector3.zero);
            baseT.localRotation = WeldPlanner.BaseRotation;
            var arm = baseT.gameObject.AddComponent<RobotArm>();
            arm.geom = g;
            Prims.Cyl("BasePlinth", baseT, new Vector3(0f, 0.09f, 0f), 0.26f, 0.18f, gray);

            var j1 = Prims.Node("J1", baseT, Vector3.zero);
            Prims.Cyl("Turret", j1, new Vector3(0f, 0.30f, 0.05f), 0.22f, 0.28f, orange);
            Prims.Box("ShoulderBlock", j1, new Vector3(0f, g.d1 - 0.02f, g.a1 * 0.6f), new Vector3(0.36f, 0.28f, 0.42f), orange);

            var j2 = Prims.Node("J2", j1, new Vector3(0f, g.d1, g.a1));
            Prims.CylX("ShoulderMotor", j2, new Vector3(0.22f, 0f, 0f), 0.13f, 0.14f, gray);
            Prims.Box("UpperArm", j2, new Vector3(0.06f, 0f, g.L2 * 0.5f), new Vector3(0.16f, 0.24f, g.L2 + 0.12f), orange);

            var j3 = Prims.Node("J3", j2, new Vector3(0f, 0f, g.L2));
            Prims.CylX("ElbowHousing", j3, new Vector3(0f, 0f, 0f), 0.14f, 0.32f, orange);
            Prims.Box("ElbowMotor", j3, new Vector3(-0.05f, 0f, -0.18f), new Vector3(0.16f, 0.16f, 0.22f), gray);
            Prims.CylZ("Forearm", j3, new Vector3(0f, 0f, g.wristSplit * 0.5f), 0.10f, g.wristSplit, orange);
            Prims.Box("WireFeeder", j3, new Vector3(0f, -0.18f, -0.05f), new Vector3(0.22f, 0.16f, 0.30f), gray);

            var j4 = Prims.Node("J4", j3, new Vector3(0f, 0f, g.wristSplit));
            Prims.CylZ("ForearmRoll", j4, new Vector3(0f, 0f, (g.L3 - g.wristSplit) * 0.5f - 0.04f), 0.075f, g.L3 - g.wristSplit - 0.08f, orange);
            var j5 = Prims.Node("J5", j4, new Vector3(0f, 0f, g.L3 - g.wristSplit));
            Prims.CylX("WristBend", j5, Vector3.zero, 0.07f, 0.17f, gray);
            var j6 = Prims.Node("J6", j5, Vector3.zero);
            Prims.CylZ("Flange", j6, new Vector3(0f, 0f, g.d6 * 0.6f), 0.05f, g.d6 * 1.2f, orange);

            // torch: body + gooseneck + copper nozzle; TCP at the wire tip
            var torch = Prims.Node("Torch", j6, new Vector3(0f, 0f, g.d6));
            Prims.CylZ("TorchBody", torch, new Vector3(0f, 0f, 0.09f), 0.032f, 0.18f, torchMat);
            Prims.CylZ("Neck", torch, new Vector3(0f, 0f, 0.23f), 0.022f, 0.10f, torchMat);
            Prims.CylZ("Nozzle", torch, new Vector3(0f, 0f, 0.32f), 0.018f, 0.08f, copper);
            Prims.CylZ("Wire", torch, new Vector3(0f, 0f, g.tool - 0.01f), 0.0015f, 0.02f, copper);
            Prims.Box("Hose", torch, new Vector3(0f, 0.05f, 0.02f), new Vector3(0.03f, 0.06f, 0.10f), cable);
            var tcp = Prims.Node("TCP", torch, new Vector3(0f, 0f, g.tool));

            arm.joints = new[] { j1, j2, j3, j4, j5, j6 };
            arm.tcp = tcp;
            arm.SetJoints(RobotGeom.Home);
            return arm;
        }

        public static WeldEffects BuildEffects(Transform tcp, Material sparkMat, Material glowMat)
        {
            var fx = tcp.gameObject.AddComponent<WeldEffects>();
            var psGo = new GameObject("Sparks");
            psGo.transform.SetParent(tcp, false);
            psGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);  // spray back toward the torch side
            var ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 4.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.55f, 0.1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.playOnAwake = true;
            var em = ps.emission;
            em.rateOverTime = 260f;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 75f;
            sh.radius = 0.005f;
            var r = psGo.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.5f;
            r.sharedMaterial = sparkMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            fx.sparks = ps;

            var lg = new GameObject("ArcLight");
            lg.transform.SetParent(tcp, false);
            lg.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.75f, 0.85f, 1f);
            l.range = 4f;
            l.intensity = 5f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            fx.arcLight = l;

            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.name = "ArcGlow";
            glow.transform.SetParent(tcp, false);
            glow.transform.localScale = Vector3.one * 0.025f;
            var gr = glow.GetComponent<MeshRenderer>();
            gr.sharedMaterial = glowMat;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            gr.enabled = false;
            fx.glow = gr;
            return fx;
        }
    }

    /// <summary>Instantiates the job's plates (and jig supports) as scaled cubes.</summary>
    public static class PlateBuilder
    {
        public static void Build(WeldJob job, Transform root, Material plateMat, Material jigMat)
        {
            Prims.ClearChildren(root);
            foreach (var p in job.plates)
            {
                var g = Prims.Box(p.name, root, p.center, new Vector3(p.length, p.thickness, p.width), p.weldable ? plateMat : jigMat, p.rotation);
                g.transform.position = p.center;
                g.transform.rotation = p.rotation;
            }
        }
    }
}
