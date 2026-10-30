using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// The level plan — the one file to edit for pacing. A run is a sequence of levels on the course (tiles from the start
    /// of level 1). Levels 1–4 teach building with missing track only (bridges, ramps, the edges, fuel); from level 5
    /// obstacles force route changes; from level 13 the plateau cycles the mixed kinds. Each level opens with
    /// <see cref="IntroTiles"/> of full middle-layer track and ends with a runway and the wall that carries the craft into
    /// the next level. The planned speed rises linearly (in distance) from <see cref="StartSpeed"/> at level 1 to
    /// <see cref="MaxSpeed"/> at the plateau and stays there.
    /// </summary>
    public static class Levels
    {
        /// <summary>Planned speed at the start of level 1 (m/s).</summary>
        public const float StartSpeed = 14f;

        /// <summary>Planned speed from the plateau level on (m/s).</summary>
        public const float MaxSpeed = 26f;

        /// <summary>Levels with a fixed kind; later levels cycle <see cref="PlateauCycle"/>.</summary>
        public const int NamedLevels = 12;

        /// <summary>First level of the plateau (full speed, every chunk tier).</summary>
        public const int PlateauLevel = 13;

        /// <summary>Highest level a save can record.</summary>
        public const int MaxLevel = 999;

        /// <summary>Tiles of full middle-layer track at the start of every level (the banner shows here).</summary>
        public const int IntroTiles = 4;

        /// <summary>Tiles of full track on every layer before the wall (a calm run-up to the checkpoint).</summary>
        public const int RunwayTiles = 2;

        /// <summary>First level with obstacles on the rails.</summary>
        public const int ObstacleLevel = 5;

        private static readonly LevelKind[] Progression =
        {
            LevelKind.Bridges,
            LevelKind.Ramps,
            LevelKind.Edges,
            LevelKind.FuelLine,
            LevelKind.Blockade,
            LevelKind.Switchback,
            LevelKind.Skyline,
            LevelKind.Gauntlet,
            LevelKind.Blockade,
            LevelKind.Switchback,
            LevelKind.Skyline,
            LevelKind.Overload,
        };

        /// <summary>Kinds the plateau cycles through, in order, from <see cref="PlateauLevel"/>.</summary>
        public static readonly LevelKind[] PlateauCycle =
        {
            LevelKind.Gauntlet,
            LevelKind.Blockade,
            LevelKind.Switchback,
            LevelKind.Skyline,
            LevelKind.Overload,
        };

        // Indexed by (int)LevelKind; literals so tools/check-strings.py sees every key used.
        private static readonly string[] NameKeys =
        {
            "level.bridges",
            "level.ramps",
            "level.edges",
            "level.fuel_line",
            "level.blockade",
            "level.switchback",
            "level.skyline",
            "level.gauntlet",
            "level.overload",
        };

        private static readonly object Lock = new object();
        private static readonly List<LevelSpec> Specs = new List<LevelSpec>(64);

        // Course tile where levels 1 … PlateauLevel start (index = level − 1).
        private static readonly long[] StartTable = new long[PlateauLevel];

        static Levels()
        {
            long start = 0;
            for (int n = 1; n <= PlateauLevel; n++)
            {
                StartTable[n - 1] = start;
                start += TilesOf(n);
            }
        }

        public static LevelKind KindOf(int number)
        {
            if (number < 1)
            {
                number = 1;
            }

            if (number <= NamedLevels)
            {
                return Progression[number - 1];
            }

            return PlateauCycle[(number - PlateauLevel) % PlateauCycle.Length];
        }

        public static string NameKey(LevelKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < NameKeys.Length ? NameKeys[i] : NameKeys[0];
        }

        /// <summary>0 at level 1 … 1 at the plateau.</summary>
        public static float TierOf(int number)
        {
            return MathUtil.Clamp01((number - 1) / (float)(PlateauLevel - 1));
        }

        /// <summary>Spec of level <paramref name="number"/> (1-based, clamped to <see cref="MaxLevel"/>; cached).</summary>
        public static LevelSpec Get(int number)
        {
            number = MathUtil.Clamp(number, 1, MaxLevel);
            lock (Lock)
            {
                while (Specs.Count < number)
                {
                    Specs.Add(Build(Specs.Count + 1));
                }

                return Specs[number - 1];
            }
        }

        /// <summary>Tiles in level <paramref name="number"/> (intro and wall included).</summary>
        public static int TilesOf(int number)
        {
            if (number <= 1)
            {
                return 44;
            }

            if (number <= 4)
            {
                return 52;
            }

            return number >= PlateauLevel ? 68 : 60;
        }

        /// <summary>Course tile where level <paramref name="number"/> starts.</summary>
        public static long StartTileOf(int number)
        {
            if (number <= 1)
            {
                return 0;
            }

            if (number <= PlateauLevel)
            {
                return StartTable[number - 1];
            }

            return StartTable[PlateauLevel - 1] + (long)(number - PlateauLevel) * TilesOf(PlateauLevel);
        }

        /// <summary>Course position (m) where level <paramref name="number"/> starts.</summary>
        public static double StartOf(int number)
        {
            return FieldSpec.TileStart(StartTileOf(number));
        }

        /// <summary>Level of course tile <paramref name="tile"/>.</summary>
        public static int LevelOfTile(long tile)
        {
            if (tile <= 0)
            {
                return 1;
            }

            long plateauStart = StartTable[PlateauLevel - 1];
            if (tile >= plateauStart)
            {
                long k = (tile - plateauStart) / TilesOf(PlateauLevel);
                return PlateauLevel + (int)Math.Min(k, 1000000L);
            }

            int n = 1;
            while (n < PlateauLevel && StartTable[n] <= tile)
            {
                n++;
            }

            return n;
        }

        /// <summary>Level at course position <paramref name="course"/> (m).</summary>
        public static int LevelAt(double course)
        {
            return LevelOfTile(FieldSpec.TileOf(Math.Max(0.0, course)));
        }

        /// <summary>Planned speed (m/s) at course position <paramref name="course"/>.</summary>
        public static float SpeedAt(double course)
        {
            if (!(course > 0.0))
            {
                return StartSpeed;
            }

            int n = LevelAt(course);
            if (n >= PlateauLevel)
            {
                return MaxSpeed;
            }

            float v0 = LevelStartSpeed(n);
            float v1 = LevelStartSpeed(n + 1);
            double f = (course - StartOf(n)) / (TilesOf(n) * (double)FieldSpec.TileLength);
            return (float)(v0 + (v1 - v0) * MathUtil.Clamp01((float)f));
        }

        /// <summary>Normalised speed for effects: 0 at the start speed, 1 at the plateau speed.</summary>
        public static float Speed01(float speed)
        {
            return (speed - StartSpeed) / (MaxSpeed - StartSpeed);
        }

        public static float LevelStartSpeed(int number)
        {
            return MathUtil.Lerp(StartSpeed, MaxSpeed, TierOf(number));
        }

        // ------------------------------------------------------------------ level specs

        private static LevelSpec Build(int number)
        {
            float t = TierOf(number);
            var s = new LevelSpec
            {
                Number = number,
                Kind = KindOf(number),
                Tier = t,
                StartSpeed = LevelStartSpeed(number),
                EndSpeed = LevelStartSpeed(number + 1),
                Tiles = TilesOf(number),
                StartTile = StartTileOf(number),
                MaxChunkTier = number <= 1 ? 0 : number <= 3 ? 1 : number <= 7 ? 2 : 3,
                TopLayer = number >= 2,
                Obstacles = number >= ObstacleLevel,
                ChunkGap = MathUtil.Lerp(1.8f, 1.1f, t),
                FuelScale = number <= 3 ? 1f : MathUtil.Lerp(0.9f, 0.7f, t),
            };

            // Weights indexed by ChunkTag: Bridge, Ramp, Edge, Obstacle, Gems, Fuel.
            switch (s.Kind)
            {
                case LevelKind.Bridges:
                    Weights(s, 1f, 0f, 0f, 0f, 0.3f, 0.3f);
                    break;
                case LevelKind.Ramps:
                    Weights(s, 0.5f, 1f, 0f, 0f, 0.6f, 0.3f);
                    break;
                case LevelKind.Edges:
                    Weights(s, 0.5f, 0.6f, 1f, 0f, 0.4f, 0.3f);
                    break;
                case LevelKind.FuelLine:
                    Weights(s, 1f, 0.6f, 0.5f, 0f, 0.4f, 0.15f);
                    s.FuelScale *= 0.85f;
                    break;
                case LevelKind.Blockade:
                    Weights(s, 0.5f, 0.5f, 0.3f, 1f, 0.3f, 0.25f);
                    break;
                case LevelKind.Switchback:
                    Weights(s, 0.5f, 1f, 0.4f, 0.8f, 0.4f, 0.25f);
                    break;
                case LevelKind.Skyline:
                    Weights(s, 0.6f, 0.6f, 1f, 0.5f, 0.8f, 0.25f);
                    break;
                case LevelKind.Overload:
                    Weights(s, 0.8f, 0.8f, 0.8f, 0.9f, 0.4f, 0.2f);
                    s.ChunkGap *= 0.8f;
                    break;
                default: // Gauntlet
                    Weights(s, 0.8f, 0.8f, 0.8f, 0.8f, 0.4f, 0.25f);
                    break;
            }

            return s;
        }

        private static void Weights(LevelSpec s, float bridge, float ramp, float edge, float obstacle, float gems,
            float fuel)
        {
            s.TagWeights[(int)ChunkTag.Bridge] = bridge;
            s.TagWeights[(int)ChunkTag.Ramp] = ramp;
            s.TagWeights[(int)ChunkTag.Edge] = edge;
            s.TagWeights[(int)ChunkTag.Obstacle] = s.Obstacles ? obstacle : 0f;
            s.TagWeights[(int)ChunkTag.Gems] = gems;
            s.TagWeights[(int)ChunkTag.Fuel] = fuel;
        }
    }
}
