namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Physical dimensions of the track and the craft, shared by gameplay and rendering (docs/ARCHITECTURE.md §4).
    /// The craft flies along +Z (the world streams toward it). The track is a grid of <see cref="LaneCount"/> lanes on X ×
    /// <see cref="LayerCount"/> layers on Y × tiles of <see cref="TileLength"/> m on Z: every (lane, layer) is one rail of
    /// light the craft rides on. Layer 0 is the bottom (the void is below it), layer 2 the top (the ceiling is above it).
    /// The lanes and layers fill the same −<see cref="HalfExtent"/> … +<see cref="HalfExtent"/> square CubeField's 3 × 3
    /// field used: rail <c>y</c> = the bottom of each row of cells.
    /// </summary>
    public static class FieldSpec
    {
        /// <summary>Cells per side of the field (3 lanes × 3 layers).</summary>
        public const int GridSize = 3;

        /// <summary>Number of lanes (columns, on X).</summary>
        public const int LaneCount = GridSize;

        /// <summary>Number of layers (rows of rails, on Y).</summary>
        public const int LayerCount = GridSize;

        /// <summary>Cells in one tile row (lanes × layers).</summary>
        public const int CellsPerRow = LaneCount * LayerCount;

        /// <summary>Middle lane (where every run starts).</summary>
        public const int CenterLane = LaneCount / 2;

        /// <summary>Middle layer (where every level starts).</summary>
        public const int MiddleLayer = LayerCount / 2;

        /// <summary>Top layer: building up from it hits the ceiling.</summary>
        public const int TopLayer = LayerCount - 1;

        /// <summary>Side of one cell = width of one lane = height between two layers (metres).</summary>
        public const float CellSize = 3f;

        /// <summary>Height between two layers of rails (m).</summary>
        public const float LayerSpacing = CellSize;

        /// <summary>Length of one track tile (m): every build fills exactly one tile.</summary>
        public const float TileLength = 8f;

        /// <summary>Half the side of the field: lanes span −HalfExtent … +HalfExtent on X, rails −HalfExtent … on Y.</summary>
        public const float HalfExtent = GridSize * CellSize * 0.5f;

        /// <summary>Height of the craft's centre above the rail it rides.</summary>
        public const float RideHeight = 0.6f;

        /// <summary>Height of the ceiling (a layer above the top rail): a ramp up from the top layer hits it.</summary>
        public const float CeilingY = -HalfExtent + LayerCount * LayerSpacing;

        /// <summary>Collision radius of the craft (a sphere; a little smaller than the hull for a forgiving feel).</summary>
        public const float ShipRadius = 0.5f;

        /// <summary>Half the craft's length along Z.</summary>
        public const float ShipHalfLength = 0.6f;

        /// <summary>Furthest the craft's centre can go from the axis on X (the centre of an outer lane).</summary>
        public const float ShipLimit = HalfExtent - CellSize * 0.5f;

        /// <summary>Half the edge of an obstacle cube (a 2.4 m cube standing on its rail, centred in its tile).</summary>
        public const float ObstacleHalfSize = 1.2f;

        /// <summary>
        /// An obstacle that passes the craft's previous lane less than this many seconds after the craft left it is a near
        /// miss: a last-moment dodge. A build that lands this close before the craft reaches its tile is a last-second build.
        /// </summary>
        public const float NearMissWindow = 0.32f;

        /// <summary>Distance (m) within which a gem or fuel cell is collected.</summary>
        public const float PickupRadius = 1.4f;

        /// <summary>Distance (m) within which a power-up is collected.</summary>
        public const float PowerUpPickupRadius = 1.6f;

        /// <summary>Lateral reach (m) of the magnet: every lane and the neighbouring layers.</summary>
        public const float MagnetRadius = CellSize * LaneCount;

        /// <summary>Top sideways speed of the craft (m/s): a lane switch takes about 0.17 s.</summary>
        public const float MaxLateralSpeed = 26f;

        /// <summary>Distance ahead (m) at which content is activated.</summary>
        public const float SpawnAhead = 190f;

        /// <summary>Distance behind (m) at which passed content is despawned.</summary>
        public const float DespawnBehind = 14f;

        /// <summary>Centre of cell <paramref name="index"/> (0 … GridSize−1) on its axis.</summary>
        public static float CellCenter(int index)
        {
            return (index - (GridSize - 1) * 0.5f) * CellSize;
        }

        /// <summary>Centre of lane <paramref name="lane"/> on X.</summary>
        public static float LaneX(int lane)
        {
            return CellCenter(lane);
        }

        /// <summary>Height of the rails of <paramref name="layer"/> (may be outside 0 … LayerCount−1: the void, the ceiling).</summary>
        public static float RailY(int layer)
        {
            return -HalfExtent + layer * LayerSpacing;
        }

        /// <summary>Height of the craft's centre riding on <paramref name="layer"/>.</summary>
        public static float RideY(int layer)
        {
            return RailY(layer) + RideHeight;
        }

        /// <summary>Height of the craft riding a level start (the middle layer).</summary>
        public static float ShipY
        {
            get { return RideY(MiddleLayer); }
        }

        /// <summary>Cell index containing <paramref name="position"/> (may be outside 0 … GridSize−1).</summary>
        public static int CellOf(float position)
        {
            return (int)System.Math.Floor((position + HalfExtent) / CellSize);
        }

        /// <summary>Nearest lane to <paramref name="x"/>, clamped to the field.</summary>
        public static int LaneOf(float x)
        {
            return MathUtil.Clamp(CellOf(x), 0, LaneCount - 1);
        }

        /// <summary>Clamps a lane index to the field.</summary>
        public static int ClampLane(int lane)
        {
            return MathUtil.Clamp(lane, 0, LaneCount - 1);
        }

        /// <summary>Clamps a craft coordinate to the field's soft limit.</summary>
        public static float ClampToField(float position)
        {
            return MathUtil.Clamp(position, -ShipLimit, ShipLimit);
        }

        /// <summary>Tile containing track position <paramref name="z"/>.</summary>
        public static long TileOf(double z)
        {
            return (long)System.Math.Floor(z / TileLength);
        }

        /// <summary>Track position where tile <paramref name="tile"/> starts.</summary>
        public static double TileStart(long tile)
        {
            return tile * (double)TileLength;
        }

        /// <summary>Index of (lane, layer) inside a tile row.</summary>
        public static int CellIndex(int lane, int layer)
        {
            return layer * LaneCount + lane;
        }
    }
}
