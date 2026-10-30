using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Diagnostics;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// The building runner's rules: lanes, building on the next tile with fuel, falls (one layer = a hull point, two or
    /// off the bottom = death), the ceiling, obstacles, fuel, the wall checkpoint, revives and determinism. Most tests
    /// start from a run whose first tiles are plain rail without pickups (<see cref="NewSim"/>) and write a small
    /// situation into them with <c>Grid.Set</c> instead of relying on what the generator happens to lay.
    /// </summary>
    public class RunSimulationTests
    {
        private const float Frame = 1f / 60f;
        private const int Lane = FieldSpec.CenterLane;
        private const int Middle = FieldSpec.MiddleLayer;

        /// <summary>Tiles at the start of a <see cref="NewSim"/> run laid as plain rail on every open layer, without pickups.</summary>
        private const int PlainTiles = 8;

        private static RunSimulation NewSim(int startLevel = 1, Action<RunConfig> configure = null)
        {
            var config = new RunConfig { Seed = 3, StartLevel = startLevel, Balance = new BalanceConfig() };
            configure?.Invoke(config);
            var sim = new RunSimulation(config);
            int open = sim.LevelSpec.TopLayer ? FieldSpec.LayerCount : FieldSpec.LayerCount - 1;
            for (long t = 0; t < PlainTiles; t++)
            {
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                    {
                        sim.Grid.Set(t, lane, layer, layer < open ? CellType.Track : CellType.Empty);
                    }
                }
            }

            ((List<Pickup>)sim.Pickups).RemoveAll(p => p.Z < FieldSpec.TileStart(PlainTiles));
            return sim;
        }

        /// <summary>Steps until <paramref name="until"/> holds, the run crashes or the frames run out; true if it held.</summary>
        private static bool Ride(RunSimulation sim, Func<RunSimulation, bool> until, List<RunEvent> log = null,
            Func<RunSimulation, RunInput> pilot = null, int maxFrames = 60 * 20, bool pave = false)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (until(sim))
                {
                    return true;
                }

                if (sim.IsCrashed)
                {
                    return false;
                }

                if (pave)
                {
                    Pave(sim);
                }

                Step(sim, pilot != null ? pilot(sim) : new RunInput(), log);
            }

            return until(sim);
        }

        private static void Step(RunSimulation sim, RunInput input, List<RunEvent> log = null)
        {
            sim.Step(Frame, input);
            log?.AddRange(sim.Events);
            sim.ClearEvents();
        }

        /// <summary>Lays plain rail on the craft's lane and layer over the next few tiles (never on the wall).</summary>
        private static void Pave(RunSimulation sim)
        {
            if (sim.Phase != RunPhase.Riding)
            {
                return;
            }

            for (long t = sim.Tile + 1; t <= sim.Tile + 3; t++)
            {
                TrackRow row = sim.Grid.Row(t);
                if (row != null && !row.IsWall && row.Get(sim.TargetLane, sim.Layer) != CellType.Track)
                {
                    sim.Grid.Set(t, sim.TargetLane, sim.Layer, CellType.Track);
                }
            }
        }

        private static int Count(List<RunEvent> log, RunEventType type)
        {
            int n = 0;
            foreach (RunEvent e in log)
            {
                n += e.Type == type ? 1 : 0;
            }

            return n;
        }

        private static RunEvent Find(List<RunEvent> log, RunEventType type)
        {
            foreach (RunEvent e in log)
            {
                if (e.Type == type)
                {
                    return e;
                }
            }

            Assert.Fail("no " + type + " event");
            return default;
        }

        /// <summary>Puts a fuel cell on (tile, lane, layer), as the generator would.</summary>
        private static Pickup PlaceFuel(RunSimulation sim, long tile, int lane, int layer, int value)
        {
            return Place(sim, PickupKind.Fuel, tile, 0.5f, lane, layer, value, null);
        }

        /// <summary>Adds a pickup above the rail of (tile, lane, layer), <paramref name="along"/> (0..1) into the tile.</summary>
        private static Pickup Place(RunSimulation sim, PickupKind kind, long tile, float along, int lane, int layer, int value,
            GemTrail trail)
        {
            var p = new Pickup
            {
                Id = 90000 + sim.Pickups.Count,
                Kind = kind,
                Lane = lane,
                Layer = layer,
                X = FieldSpec.LaneX(lane),
                Y = FieldSpec.RailY(layer) + TrackGenerator.PickupHeight,
                Z = FieldSpec.TileStart(tile) + FieldSpec.TileLength * along,
                Value = value,
                Group = trail,
            };
            ((List<Pickup>)sim.Pickups).Add(p); // straight into the active list
            return p;
        }

        // ------------------------------------------------------------------ lanes

        [Test]
        public void Swipes_MoveOneLaneAtATime_AndStayInTheField()
        {
            RunSimulation sim = NewSim();
            Assert.AreEqual(FieldSpec.CenterLane, sim.Lane);
            Assert.AreEqual(Middle, sim.Layer, "every level starts on the middle layer");
            sim.Step(Frame, new RunInput { LaneShift = -1 });
            for (int i = 0; i < 30; i++)
            {
                sim.Step(Frame, new RunInput());
            }

            Assert.AreEqual(0, sim.Lane);
            Assert.AreEqual(FieldSpec.LaneX(0), sim.ShipX, 1e-4f, "settles on the lane's centre");

            sim.Step(Frame, new RunInput { LaneShift = -1 });
            Assert.AreEqual(0, sim.TargetLane, "no lane left of the left lane");

            sim.Step(Frame, new RunInput { LaneShift = 2 });
            float t = 0f;
            while (sim.ShipX < FieldSpec.LaneX(2) - 0.01f && t < 1f)
            {
                sim.Step(Frame, new RunInput());
                t += Frame;
            }

            Assert.That(t, Is.LessThan(0.45f), "two lanes in well under half a second");
            Assert.AreEqual(Middle, sim.Layer);
            Assert.IsFalse(sim.IsFalling);
        }

        [Test]
        public void LaneSwitch_WaitsWhileOnARamp()
        {
            RunSimulation sim = NewSim(2);
            Step(sim, new RunInput { Build = BuildKind.RampUp });
            Assert.IsTrue(Ride(sim, s => s.Tile == 1 && s.Distance > FieldSpec.TileStart(1) + 2.0));
            Step(sim, new RunInput { LaneShift = -1 });
            Assert.AreEqual(Lane, sim.TargetLane, "no lane switch halfway up a ramp");
            Assert.IsTrue(Ride(sim, s => s.Tile == 2));
            Ride(sim, s => s.TargetLane != Lane, maxFrames: 5);
            Assert.AreEqual(Lane - 1, sim.TargetLane, "the queued switch happens on the next tile");
            Assert.AreEqual(Middle + 1, sim.Layer, "after climbing the ramp");
        }

        // ------------------------------------------------------------------ building

        [Test]
        public void Tap_OnAGap_BuildsABridgeOnTheNextTile_ForTheBridgePrice()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            Assert.AreEqual(1, sim.GapAhead(), "the ghost outline warns of the gap");
            long tile;
            int lane;
            int layer;
            Assert.IsTrue(sim.BuildTarget(out tile, out lane, out layer));
            Assert.AreEqual(1, tile, "builds land on the next tile");
            Assert.AreEqual(Lane, lane);
            Assert.AreEqual(Middle, layer);

            float fuel = sim.Fuel;
            float price = sim.BuildCost(BuildKind.Bridge);
            Assert.AreEqual(sim.Ship.BridgeCost, price, 1e-5f);
            Assert.AreEqual(new BalanceConfig().BridgeCost, price, 1e-5f);
            var log = new List<RunEvent>();
            Step(sim, new RunInput { Build = BuildKind.Bridge }, log);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(1, Lane, Middle));
            Assert.IsTrue(sim.Grid.Row(1).IsBuilt(Lane, Middle));
            Assert.AreEqual(fuel - price, sim.Fuel, 1e-4f);
            Assert.AreEqual(-1, sim.GapAhead());
            RunEvent built = Find(log, RunEventType.Built);
            Assert.AreEqual(1, built.Id, "tile");
            Assert.AreEqual((int)BuildKind.Bridge, built.Count);
            Assert.AreEqual(price, built.Value, 1e-5f);

            Assert.IsTrue(Ride(sim, s => s.Tile == 3, log));
            Assert.IsFalse(sim.IsFalling);
            Assert.AreEqual(Middle, sim.Layer, "the craft rode across on the bridge");
            Assert.AreEqual(sim.MaxHull, sim.Hull);
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.Builds);
            Assert.AreEqual(1, r.Bridges);
            Assert.AreEqual(1, r.GapsBridged);
            Assert.AreEqual(price, r.FuelSpent, 1e-4f);
            Assert.AreEqual(0f, r.FuelWasted);
            Assert.AreEqual(0, r.Falls);
            Assert.AreEqual(0, Count(log, RunEventType.FellOff));
        }

        [Test]
        public void BuildingOnRail_ThatIsAlreadyThere_WastesTheFuel()
        {
            RunSimulation sim = NewSim();
            Step(sim, new RunInput { Build = BuildKind.Bridge });
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.Builds);
            Assert.AreEqual(0, r.GapsBridged, "nothing was missing");
            Assert.AreEqual(sim.Ship.BridgeCost, r.FuelWasted, 1e-4f);
        }

        [Test]
        public void ABuild_WithTooLittleFuel_IsRefused_AndCostsNothing()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            sim.DebugSetFuel(sim.Ship.BridgeCost - 0.5f);
            float fuel = sim.Fuel;
            Assert.IsFalse(sim.CanAfford(BuildKind.Bridge));
            Assert.IsTrue(sim.CanAfford(BuildKind.RampDown), "the cheap ramp down is still affordable");
            var log = new List<RunEvent>();
            Step(sim, new RunInput { Build = BuildKind.Bridge }, log);
            Assert.AreEqual(CellType.Empty, sim.Grid.Get(1, Lane, Middle), "nothing was built");
            Assert.AreEqual(fuel, sim.Fuel, 1e-5f);
            Assert.AreEqual((int)BuildKind.Bridge, Find(log, RunEventType.BuildFailed).Count);
            Assert.AreEqual(0, Count(log, RunEventType.Built));
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.EmptyBuilds);
            Assert.AreEqual(0, r.Builds);
            Assert.AreEqual(0f, r.FuelSpent);
        }

        [Test]
        public void InstantBuild_MakesBuildingFree()
        {
            RunSimulation sim = NewSim();
            sim.DebugSetFuel(0f);
            sim.DebugGrant(PickupKind.InstantBuild);
            Assert.That(sim.InstantBuildRemaining, Is.GreaterThan(0f));
            Assert.AreEqual(0f, sim.BuildCost(BuildKind.RampUp));
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            Step(sim, new RunInput { Build = BuildKind.Bridge });
            Assert.AreEqual(CellType.Track, sim.Grid.Get(1, Lane, Middle));
            Assert.AreEqual(0f, sim.Fuel);
            Assert.AreEqual(1, sim.BuildResult().Builds);
        }

        // ------------------------------------------------------------------ falls

        [Test]
        public void RidingIntoAGap_DropsOneLayer_AndCostsAHullPoint()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.IsFalling, log), "the craft leaves its rail");
            Assert.AreEqual(1, sim.Tile);
            Assert.AreEqual(Middle, Find(log, RunEventType.FellOff).Count, "fell from the middle layer");
            Assert.IsTrue(Ride(sim, s => s.Phase == RunPhase.Riding, log, maxFrames: 60));
            Assert.AreEqual(Middle - 1, sim.Layer, "landed on the layer below");
            Assert.AreEqual(FieldSpec.RideY(Middle - 1), sim.ShipY, 1e-3f);
            Assert.AreEqual(sim.MaxHull - 1, sim.Hull);
            Assert.AreEqual(sim.Hull, Find(log, RunEventType.Damaged).Count);
            RunEvent landed = Find(log, RunEventType.Landed);
            Assert.AreEqual(sim.Hull, landed.Count);
            Assert.AreEqual(0f, landed.Value, "no shield took it");

            Assert.IsTrue(Ride(sim, s => s.Tile == 4, log));
            Assert.IsFalse(sim.IsCrashed);
            Assert.AreEqual(Middle - 1, sim.Layer, "rides on along the lower layer");
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.Falls);
            Assert.AreEqual(1, r.DamageTaken);
        }

        [Test]
        public void AShield_AbsorbsAOneLayerFall()
        {
            RunSimulation sim = NewSim();
            sim.DebugGrantShield();
            Assert.IsTrue(sim.ShieldActive);
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.Tile == 3, log));
            Assert.AreEqual(Middle - 1, sim.Layer, "still dropped a layer");
            Assert.AreEqual(sim.MaxHull, sim.Hull, "but the hull is intact");
            Assert.IsFalse(sim.ShieldActive);
            Assert.AreEqual(1f, Find(log, RunEventType.Landed).Value, "the shield took it");
            Assert.AreEqual(1, Count(log, RunEventType.ShieldBroken));
            Assert.AreEqual(0, Count(log, RunEventType.Damaged));
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.Falls);
            Assert.AreEqual(0, r.DamageTaken);
            Assert.AreEqual(1, r.ShieldsBroken);
        }

        [Test]
        public void AFall_OnTheLastHullPoint_BreaksTheHull()
        {
            RunSimulation sim = NewSim(1, c =>
            {
                c.Ship = ShipStats.FromLevels(null, c.Balance);
                c.Ship.MaxHull = 1;
            });
            Assert.AreEqual(1, sim.Hull);
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            Ride(sim, s => s.Tile == 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.HullBroken, sim.BuildResult().Cause);
            Assert.AreEqual(0, sim.Hull);
        }

        [Test]
        public void FallingOffTheBottomLayer_IsTheVoid()
        {
            RunSimulation sim = NewSim();
            var log = new List<RunEvent>();
            Step(sim, new RunInput { Build = BuildKind.RampDown }, log);
            Assert.AreEqual(CellType.RampDown, sim.Grid.Get(1, Lane, Middle));
            Assert.AreEqual(sim.Ship.RampDownCost, sim.BuildResult().FuelSpent, 1e-4f);
            sim.Grid.Set(2, Lane, 0, CellType.Empty);
            Ride(sim, s => s.IsCrashed, log, maxFrames: 60 * 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(0, Find(log, RunEventType.FellOff).Count, "fell from the bottom layer");
            Assert.AreEqual(CrashCause.Void, sim.BuildResult().Cause);
            Assert.AreEqual((int)CrashCause.Void, Find(log, RunEventType.Crashed).Count);
            Assert.AreEqual(0, sim.BuildResult().Falls, "no fall survived");
        }

        [Test]
        public void ARampDownFromTheBottomLayer_LeadsIntoTheVoid()
        {
            RunSimulation sim = NewSim();
            Assert.IsTrue(Ride(sim, s => s.IsCrashed, null, RunAutopilot.SeekCrash, 60 * 10));
            Assert.AreEqual(CrashCause.Void, sim.BuildResult().Cause);
            Assert.AreEqual(2, sim.BuildResult().RampsDown, "middle → bottom → void");
            Assert.IsTrue(sim.CanRevive);
        }

        [Test]
        public void FallingTwoLayers_IsALongFall()
        {
            RunSimulation sim = NewSim();
            for (long t = 1; t <= 2; t++)
            {
                sim.Grid.Set(t, Lane, Middle, CellType.Empty);
                sim.Grid.Set(t, Lane, 0, CellType.Empty);
            }

            Ride(sim, s => s.IsCrashed, maxFrames: 60 * 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.LongFall, sim.BuildResult().Cause);
            Assert.AreEqual(sim.MaxHull, sim.Hull, "a long fall kills outright, not through the hull");
        }

        [Test]
        public void AShield_DoesNotSaveALongFall_OrTheVoid()
        {
            RunSimulation sim = NewSim();
            sim.DebugGrantShield();
            for (long t = 1; t <= 2; t++)
            {
                sim.Grid.Set(t, Lane, Middle, CellType.Empty);
                sim.Grid.Set(t, Lane, 0, CellType.Empty);
            }

            Ride(sim, s => s.IsCrashed, maxFrames: 60 * 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.LongFall, sim.BuildResult().Cause, "the design doc: a shield does not block a two-layer fall");

            sim = NewSim();
            sim.DebugGrantShield();
            Step(sim, new RunInput { Build = BuildKind.RampDown });
            sim.Grid.Set(2, Lane, 0, CellType.Empty);
            Ride(sim, s => s.IsCrashed, maxFrames: 60 * 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.Void, sim.BuildResult().Cause, "nor a fall off the bottom layer");
        }

        // ------------------------------------------------------------------ ramps and the ceiling

        [Test]
        public void Ramps_ChangeLayerAtTheTileEnd()
        {
            RunSimulation sim = NewSim(2);
            var log = new List<RunEvent>();
            float fuel = sim.Fuel;
            Step(sim, new RunInput { Build = BuildKind.RampUp }, log);
            Assert.AreEqual(CellType.RampUp, sim.Grid.Get(1, Lane, Middle));
            Assert.AreEqual(fuel - sim.Ship.RampUpCost, sim.Fuel, 1e-4f);

            Assert.IsTrue(Ride(sim, s => s.Distance > FieldSpec.TileStart(1) + FieldSpec.TileLength * 0.5, log));
            Assert.AreEqual(1, sim.Tile);
            Assert.AreEqual(Middle, sim.Layer, "still on the layer it entered the tile on");
            Assert.That(sim.ShipY, Is.InRange(FieldSpec.RideY(Middle) + 0.5f, FieldSpec.RideY(Middle + 1) - 0.5f), "halfway up");
            Assert.AreEqual(0, Count(log, RunEventType.LayerChanged));

            Assert.IsTrue(Ride(sim, s => s.Tile == 2, log));
            Assert.AreEqual(Middle + 1, sim.Layer, "on the top layer from the next tile");
            RunEvent changed = Find(log, RunEventType.LayerChanged);
            Assert.AreEqual(Middle + 1, changed.Count);
            Assert.AreEqual(1f, changed.Value);
            Assert.AreEqual(FieldSpec.RideY(Middle + 1), sim.ShipY, 0.05f);

            // And down again: swipe down builds a ramp down on the next tile.
            fuel = sim.Fuel;
            Step(sim, new RunInput { Build = BuildKind.RampDown }, log);
            Assert.AreEqual(CellType.RampDown, sim.Grid.Get(3, Lane, Middle + 1));
            Assert.AreEqual(fuel - sim.Ship.RampDownCost, sim.Fuel, 1e-4f);
            Assert.IsTrue(Ride(sim, s => s.Tile == 4, log));
            Assert.AreEqual(Middle, sim.Layer);
            Assert.IsFalse(sim.IsFalling);
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.RampsUp);
            Assert.AreEqual(1, r.RampsDown);
            Assert.AreEqual(2, r.Builds);
            Assert.AreEqual(0, r.Falls);
            Assert.That(r.TopLayerSeconds, Is.GreaterThan(0f));
        }

        [Test]
        public void ARampUpFromTheTopLayer_HitsTheCeiling()
        {
            RunSimulation sim = ClimbToTheTop();
            Assert.IsFalse(sim.ShieldActive);
            var log = new List<RunEvent>();
            Step(sim, new RunInput { Build = BuildKind.RampUp }, log);
            Assert.AreEqual(CellType.RampUp, sim.Grid.Get(3, Lane, FieldSpec.TopLayer), "the build itself is allowed");
            Ride(sim, s => s.IsCrashed, log, maxFrames: 60 * 2);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(3, sim.Tile, "as the craft meets the ramp");
            Assert.AreEqual(CrashCause.Ceiling, sim.BuildResult().Cause);
            Assert.AreEqual((int)CrashCause.Ceiling, Find(log, RunEventType.Crashed).Count);
        }

        [Test]
        public void AShield_AbsorbsTheCeiling()
        {
            RunSimulation sim = ClimbToTheTop();
            sim.DebugGrantShield();
            var log = new List<RunEvent>();
            Step(sim, new RunInput { Build = BuildKind.RampUp }, log);
            Assert.IsTrue(Ride(sim, s => s.Tile == 4, log));
            Assert.IsFalse(sim.IsCrashed);
            Assert.IsFalse(sim.ShieldActive);
            Assert.AreEqual((int)CrashCause.Ceiling, Find(log, RunEventType.ShieldBroken).Count);
            Assert.AreEqual(FieldSpec.TopLayer, sim.Layer, "flattened against the ceiling, still on top");
            Assert.AreEqual(CellType.Track, sim.Grid.Get(3, Lane, FieldSpec.TopLayer));
        }

        /// <summary>A level-2 run (top layer open) whose craft has ramped up to the top layer and is on tile 2.</summary>
        private static RunSimulation ClimbToTheTop()
        {
            RunSimulation sim = NewSim(2);
            Assert.IsTrue(sim.LevelSpec.TopLayer);
            Step(sim, new RunInput { Build = BuildKind.RampUp });
            Assert.IsTrue(Ride(sim, s => s.Tile == 2));
            Assert.AreEqual(FieldSpec.TopLayer, sim.Layer);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(3, Lane, FieldSpec.TopLayer));
            return sim;
        }

        // ------------------------------------------------------------------ obstacles

        [Test]
        public void RidingIntoAnObstacle_Crashes()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(2, Lane, Middle, CellType.Obstacle);
            var log = new List<RunEvent>();
            Ride(sim, s => s.IsCrashed, log, maxFrames: 60 * 3);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(2, sim.Tile);
            Assert.AreEqual(CrashCause.Obstacle, sim.BuildResult().Cause);
            Assert.AreEqual((int)CrashCause.Obstacle, Find(log, RunEventType.Crashed).Count);
        }

        [Test]
        public void AnObstacleInAnotherLane_IsDodged()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(2, Lane + 1, Middle, CellType.Obstacle);
            sim.Grid.Set(2, Lane, Middle - 1, CellType.Obstacle);
            Assert.IsTrue(Ride(sim, s => s.Tile == 4));
            Assert.IsFalse(sim.IsCrashed);
            Assert.AreEqual(2, sim.BuildResult().CubesDodged);
        }

        [Test]
        public void BuildingIntoAnObstacle_Crashes()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Obstacle);
            float fuel = sim.Fuel;
            Step(sim, new RunInput { Build = BuildKind.Bridge });
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.Obstacle, sim.BuildResult().Cause);
            Assert.AreEqual(fuel, sim.Fuel, 1e-5f);
        }

        [Test]
        public void Overload_SmashesObstacles()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(2, Lane, Middle, CellType.Obstacle);
            sim.DebugGrant(PickupKind.Overload);
            Assert.IsTrue(sim.OverloadActive);
            Assert.IsTrue(sim.Hovering);
            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.Tile == 4, log));
            Assert.IsFalse(sim.IsCrashed);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(2, Lane, Middle), "the cube is gone");
            RunEvent smashed = Find(log, RunEventType.Smashed);
            Assert.AreEqual(2, smashed.Id);
            Assert.AreEqual(FieldSpec.CellIndex(Lane, Middle), smashed.Count);
            Assert.That(Count(log, RunEventType.Bonus), Is.GreaterThan(0), "smashing pays");
            Assert.That(sim.Speed, Is.GreaterThan(sim.Course.SpeedAt(sim.Distance) * 1.05f), "Overload is a speed burst");
            RunResult r = sim.BuildResult();
            Assert.AreEqual(1, r.Smashed);
            Assert.AreEqual(1, r.Overloads);
            Assert.AreEqual(0, r.ShieldsBroken);
        }

        [Test]
        public void Overload_FliesOverGaps()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Empty);
            sim.Grid.Set(2, Lane, Middle, CellType.Empty);
            sim.DebugGrant(PickupKind.Overload);
            Assert.IsTrue(Ride(sim, s => s.Tile == 4));
            Assert.AreEqual(Middle, sim.Layer);
            Assert.AreEqual(0, sim.BuildResult().Falls);
        }

        [Test]
        public void AShield_BreaksOnAnObstacle_Instead()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(2, Lane, Middle, CellType.Obstacle);
            sim.DebugGrantShield();
            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.Tile == 4, log));
            Assert.IsFalse(sim.IsCrashed);
            Assert.IsFalse(sim.ShieldActive);
            Assert.AreEqual((int)CrashCause.Obstacle, Find(log, RunEventType.ShieldBroken).Count);
            Assert.AreEqual(1, sim.BuildResult().ShieldsBroken);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(2, Lane, Middle));
        }

        [Test]
        public void Invincible_NeverCrashes()
        {
            RunSimulation sim = NewSim(1, c => c.Invincible = true);
            sim.Grid.Set(2, Lane, 0, CellType.Obstacle);
            for (int i = 0; i < 60 * 10; i++)
            {
                Step(sim, RunAutopilot.SeekCrash(sim));
                Assert.IsFalse(sim.IsCrashed, "frame " + i);
            }

            RunResult r = sim.BuildResult();
            Assert.That(r.RampsDown, Is.GreaterThan(1), "it kept diving into the void");
            Assert.That(r.Falls, Is.GreaterThan(0), "and was caught every time");
            Assert.AreEqual(0, r.DamageTaken);
            Assert.That(r.Smashed, Is.GreaterThan(0), "building into the cube shattered it");
        }

        // ------------------------------------------------------------------ fuel

        [Test]
        public void FuelCells_AddFuel_UpToTheTank()
        {
            RunSimulation sim = NewSim();
            float fuel = sim.Fuel;
            var log = new List<RunEvent>();
            PlaceFuel(sim, 1, Lane, Middle, 3);
            Assert.IsTrue(Ride(sim, s => s.Tile == 2, log));
            Assert.AreEqual(fuel + 3f, sim.Fuel, 1e-4f);
            RunEvent got = Find(log, RunEventType.PickupCollected);
            Assert.AreEqual((int)PickupKind.Fuel, got.Count);
            Assert.AreEqual(3f, got.Value);
            Assert.AreEqual(3f, sim.BuildResult().FuelCollected, 1e-4f);

            sim.DebugSetFuel(sim.MaxFuel - 1f);
            PlaceFuel(sim, 3, Lane, Middle, 3);
            Assert.IsTrue(Ride(sim, s => s.Tile == 4));
            Assert.AreEqual(sim.MaxFuel, sim.Fuel, 1e-4f, "capped at the tank");
            Assert.AreEqual(4f, sim.BuildResult().FuelCollected, 1e-4f, "only what fitted counts");

            PlaceFuel(sim, 5, Lane + 1, Middle, 3);
            sim.DebugSetFuel(1f);
            Assert.IsTrue(Ride(sim, s => s.Distance > FieldSpec.TileStart(5) + FieldSpec.TileLength * 0.75));
            Assert.AreEqual(1f, sim.Fuel, 1e-4f, "a cell in another lane is missed");
        }

        // ------------------------------------------------------------------ the wall checkpoint

        [Test]
        public void TheWall_PausesAtTheCheckpoint_AndContinueStartsTheNextLevelOnTheMiddleLayer()
        {
            RunSimulation sim = NewSim();
            long wall = sim.Course.WallTile(1);
            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.Phase == RunPhase.Climbing, log, maxFrames: 60 * 60, pave: true), "rides to the wall");
            Assert.AreEqual(wall, sim.Tile);
            Assert.AreEqual(1, Find(log, RunEventType.WallReached).Count);
            Assert.IsTrue(Ride(sim, s => s.AtCheckpoint, log, maxFrames: 120));
            Assert.AreEqual(RunPhase.Checkpoint, sim.Phase);
            Assert.AreEqual(1, Find(log, RunEventType.CheckpointReached).Count);
            Assert.AreEqual(1, Find(log, RunEventType.LevelCleared).Count);
            Assert.AreEqual(1, sim.Level);
            Assert.AreEqual(1, sim.BuildResult().LevelsCleared);
            long score = sim.Score;
            Assert.That(score, Is.GreaterThan(0));

            // The run waits for the pilot.
            double distance = sim.Distance;
            double time = sim.Time;
            long tile;
            int lane;
            int layer;
            Assert.IsFalse(sim.BuildTarget(out tile, out lane, out layer), "no building at a checkpoint");
            for (int i = 0; i < 60 * 5; i++)
            {
                Step(sim, new RunInput { LaneShift = 1, Build = BuildKind.Bridge });
            }

            Assert.IsTrue(sim.AtCheckpoint);
            Assert.AreEqual(distance, sim.Distance);
            Assert.AreEqual(time, sim.Time, "paused time is not run time");
            Assert.AreEqual(0, sim.BuildResult().Builds);

            sim.DebugSetFuel(1f);
            Assert.IsTrue(sim.GrantPerk(CheckpointPerk.Shield));
            Assert.IsTrue(sim.ShieldActive);
            Assert.IsTrue(sim.ContinueFromCheckpoint(CheckpointPerk.Refuel));
            Assert.AreEqual(sim.MaxFuel, sim.Fuel, "the refuel perk");
            Assert.IsFalse(sim.ContinueFromCheckpoint(CheckpointPerk.Refuel), "only once");
            Assert.IsFalse(sim.GrantPerk(CheckpointPerk.Patch), "perks only at the checkpoint");
            Assert.AreEqual(RunPhase.Descending, sim.Phase);
            Assert.AreEqual(2, sim.Level);
            log.Clear();
            Assert.IsTrue(Ride(sim, s => s.Phase == RunPhase.Riding, log, maxFrames: 120));
            Assert.AreEqual(sim.Course.LevelStartTile(2), sim.Tile, "lands on the first tile of level 2");
            Assert.AreEqual(Middle, sim.Layer);
            Assert.AreEqual(FieldSpec.RideY(Middle), sim.ShipY, 1e-4f);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(sim.Tile, sim.Lane, Middle));
            Assert.IsTrue(Ride(sim, s => s.Tile == sim.Course.LevelStartTile(2) + 3, log));
            Assert.IsFalse(sim.IsFalling);
            Assert.AreEqual(2, sim.BuildResult().LevelReached);
        }

        [Test]
        public void AutoCheckpoint_ContinuesByItself()
        {
            RunSimulation sim = NewSim(1, c => c.AutoCheckpoint = true);
            var log = new List<RunEvent>();
            int paused = 0;
            Assert.IsTrue(Ride(sim, s =>
            {
                paused += s.AtCheckpoint ? 1 : 0;
                return s.Level == 2 && s.Phase == RunPhase.Riding;
            }, log, maxFrames: 60 * 60, pave: true));
            Assert.AreEqual(1, Count(log, RunEventType.CheckpointReached));
            Assert.AreEqual(2, Count(log, RunEventType.LevelStarted), "level 1 started, then level 2");
            float wait = RunSimulation.AutoCheckpointSeconds;
            Assert.That(paused * Frame, Is.InRange(wait - 2 * Frame, wait + 2 * Frame), "a short pause at the checkpoint");
            Assert.AreEqual(Middle, sim.Layer);
            Assert.AreEqual(1, sim.BuildResult().LevelsCleared);
        }

        // ------------------------------------------------------------------ revive

        [Test]
        public void Revive_RestoresTheHull_AndPutsTheCraftOnARail()
        {
            RunSimulation sim = NewSim();
            sim.Grid.Set(1, Lane, Middle, CellType.Empty); // one layer down: a hull point
            sim.Grid.Set(3, Lane, 0, CellType.Empty); // then off the bottom
            Ride(sim, s => s.IsCrashed, maxFrames: 60 * 4);
            Assert.IsTrue(sim.IsCrashed);
            Assert.AreEqual(CrashCause.Void, sim.BuildResult().Cause);
            Assert.AreEqual(sim.MaxHull - 1, sim.Hull);
            Assert.IsTrue(sim.CanRevive);

            var log = new List<RunEvent>();
            float fuel = sim.Fuel;
            Assert.IsTrue(sim.Revive());
            log.AddRange(sim.Events);
            sim.ClearEvents();
            Assert.IsFalse(sim.IsCrashed);
            Assert.IsFalse(sim.CanRevive, "one revive per run");
            Assert.IsFalse(sim.Revive());
            Assert.AreEqual(1, Count(log, RunEventType.Revived));
            Assert.AreEqual(sim.MaxHull, sim.Hull, "hull restored");
            Assert.That(sim.Fuel, Is.GreaterThanOrEqualTo(fuel));
            Assert.AreEqual(RunPhase.Riding, sim.Phase);
            Assert.AreEqual(Middle, sim.Layer, "back on the middle layer after a fall");
            Assert.AreEqual(FieldSpec.RideY(Middle), sim.ShipY, 1e-4f);
            Assert.AreEqual(CellType.Track, sim.Grid.Get(sim.Tile, sim.Lane, sim.Layer), "a rail under the craft");
            Assert.AreEqual(CellType.Track, sim.Grid.Get(sim.Tile + 1, sim.Lane, sim.Layer));
            Assert.IsTrue(sim.Invulnerable);
            Assert.IsTrue(sim.BuildResult().Revived);

            long from = sim.Tile;
            Assert.IsTrue(Ride(sim, s => s.Tile == from + 3));
            Assert.IsFalse(sim.IsFalling);
            Assert.AreEqual(Middle, sim.Layer);
        }

        // ------------------------------------------------------------------ whole runs

        [Test]
        public void SameSeedSameInputs_SameRun()
        {
            RunSimulation a = Scripted(77);
            RunSimulation b = Scripted(77);
            Assert.That(a.Level, Is.GreaterThan(3), "the script reached a wall");
            Assert.That(a.BuildResult().Builds, Is.GreaterThan(2), "the script built");
            Assert.That(a.BuildResult().EmptyBuilds, Is.GreaterThan(0), "and ran dry");
            Assert.AreEqual(a.Distance, b.Distance);
            Assert.AreEqual(a.Time, b.Time);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.Gems, b.Gems);
            Assert.AreEqual(a.Fuel, b.Fuel);
            Assert.AreEqual(a.Hull, b.Hull);
            Assert.AreEqual(a.ShipX, b.ShipX);
            Assert.AreEqual(a.ShipY, b.ShipY);
            Assert.AreEqual(a.Layer, b.Layer);
            Assert.AreEqual(a.Level, b.Level);
            RunResult ra = a.BuildResult();
            RunResult rb = b.BuildResult();
            Assert.AreEqual(ra.Builds, rb.Builds);
            Assert.AreEqual(ra.FuelSpent, rb.FuelSpent);
            Assert.AreEqual(ra.FuelCollected, rb.FuelCollected);
            Assert.AreEqual(ra.Falls, rb.Falls);
            Assert.AreEqual(ra.EmptyBuilds, rb.EmptyBuilds);
            Assert.AreEqual(a.Grid.FirstTile, b.Grid.FirstTile);
            for (long t = a.Grid.FirstTile; t < a.Grid.EndTile; t++)
            {
                CollectionAssert.AreEqual(a.Grid.Row(t).Cells, b.Grid.Row(t).Cells, "tile " + t);
            }

            RunSimulation c = Scripted(78);
            Assert.AreNotEqual(a.Score, c.Score, "another seed, another run");
        }

        /// <summary>
        /// A minute of a level-3 run (invincible, automatic checkpoints) under a fixed script of swipes and taps that
        /// depends only on the frame number.
        /// </summary>
        private static RunSimulation Scripted(int seed)
        {
            RunSimulation sim = NewSim(3, c =>
            {
                c.Seed = seed;
                c.Invincible = true;
                c.AutoCheckpoint = true;
            });
            var script = new Rng(5);
            for (int i = 0; i < 60 * 60; i++)
            {
                var input = new RunInput();
                if (script.Chance(0.04f))
                {
                    input.LaneShift = script.Chance(0.5f) ? -1 : 1;
                }

                if (script.Chance(0.03f))
                {
                    input.Build = (BuildKind)script.Range(1, 4);
                }

                Step(sim, input);
            }

            return sim;
        }

        [Test]
        public void TheBot_ClearsLevelsOneToThree_WithoutUpgrades()
        {
            // The first runs must be winnable without upgrades: the route-planning bot builds its way through level 3.
            // It plans every frame, so it flies at 30 fps (a real device rate; the simulation sub-steps either way).
            var failures = new List<string>();
            for (int seed = 1; seed <= 6; seed++)
            {
                RunSimulation sim = Bot(seed, 1, 150f, s => s.Level >= 4);
                if (sim.IsCrashed || sim.Level < 4)
                {
                    RunResult r = sim.BuildResult();
                    failures.Add("seed " + seed + (sim.IsCrashed ? " crashed (" + r.Cause + ")" : " stalled") + " at " +
                                 sim.Distance.ToString("F0") + " m, level " + sim.Level + ", tile " + sim.Tile + ", layer " +
                                 sim.Layer + ", fuel " + sim.Fuel.ToString("F1") + ", falls " + r.Falls);
                }
            }

            Assert.IsEmpty(failures, string.Join("; ", failures));
        }

        [Test]
        public void Gems_PayTheirValue_AndAFullTrailPaysABonus()
        {
            RunSimulation sim = NewSim();
            var full = new GemTrail { Id = 1, Size = 4 };
            var broken = new GemTrail { Id = 2, Size = 4 };
            for (int k = 0; k < 4; k++)
            {
                Place(sim, PickupKind.Gem, 1 + k / 2, 0.25f + 0.5f * (k % 2), Lane, Middle, 1 + k % 2, full);
                Place(sim, PickupKind.Gem, 3 + k / 2, 0.25f + 0.5f * (k % 2), k == 3 ? Lane + 1 : Lane, Middle, 2, broken);
            }

            var log = new List<RunEvent>();
            Assert.IsTrue(Ride(sim, s => s.Tile == 3, log));
            Assert.AreEqual(6, sim.Gems, "1 + 2 + 1 + 2");
            RunEvent trail = Find(log, RunEventType.TrailCompleted);
            Assert.AreEqual(1, trail.Id);
            Assert.AreEqual(4f, trail.Value, "gems in the line");
            Assert.That(trail.Count, Is.GreaterThan(0), "bonus points");

            Assert.IsTrue(Ride(sim, s => s.Distance > FieldSpec.TileStart(5) + 6.0, log));
            Assert.AreEqual(6 + 3 * 2, sim.Gems, "the gem in the other lane was missed");
            Assert.AreEqual(1, Count(log, RunEventType.TrailCompleted), "a line with a gap pays no bonus");
            int sum = 0;
            foreach (RunEvent e in log)
            {
                if (e.Type == RunEventType.PickupCollected && e.Count == (int)PickupKind.Gem)
                {
                    sum += (int)e.Value;
                }
            }

            Assert.AreEqual(sim.Gems, sum, "every gem arrives with its value");
            RunResult r = sim.BuildResult();
            Assert.AreEqual(sim.Gems, r.Gems);
            Assert.AreEqual(1, r.FullTrails);
        }

        /// <summary>
        /// Flies the route-planning bot (Standard rating, automatic checkpoints, 30 fps) until <paramref name="until"/>
        /// holds, it crashes or <paramref name="seconds"/> of frames have passed.
        /// </summary>
        private static RunSimulation Bot(int seed, int startLevel, float seconds, Func<RunSimulation, bool> until)
        {
            const float frame = 1f / 30f;
            var sim = new RunSimulation(new RunConfig
            {
                Seed = seed,
                StartLevel = startLevel,
                Modifiers = DifficultyModifiers.Standard,
                AutoCheckpoint = true,
                Balance = new BalanceConfig(),
            });
            for (int i = 0; i < seconds / frame && !sim.IsCrashed && !until(sim); i++)
            {
                sim.Step(frame, RunAutopilot.FollowLine(sim));
                sim.ClearEvents();
            }

            return sim;
        }
    }
}
