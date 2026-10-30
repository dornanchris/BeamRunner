using BeamRunner.Core.Field;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Unity-side world helpers for the open play field, matching docs/ARCHITECTURE.md §4: the craft flies along +Z
    /// and stays at world z ≈ 0 (the world streams toward it); X is right, Y is up. The field is
    /// <see cref="FieldSpec.GridSize"/> × <see cref="FieldSpec.GridSize"/> cells of <see cref="FieldSpec.CellSize"/> m
    /// centred on the Z axis, so it spans −<see cref="FieldSpec.HalfExtent"/> … +<see cref="FieldSpec.HalfExtent"/> on X
    /// and Y. Collision is computed in exactly these coordinates (there is no curved-world bend).
    /// </summary>
    public static class FieldMath
    {
        /// <summary>Half the side of the play field (m).</summary>
        public const float HalfExtent = FieldSpec.HalfExtent;

        /// <summary>Side of one cell (m).</summary>
        public const float CellSize = FieldSpec.CellSize;

        /// <summary>Depth ahead of the craft that can hold content (it spawns this far ahead, inside the fog).</summary>
        public const float VisibleLength = FieldSpec.SpawnAhead;

        /// <summary>Height of the floor grid: far below the bottom layer, so the void under the rails reads as a drop.</summary>
        public const float FloorY = -36f;

        /// <summary>Height of the (see-through) ceiling grid: the ceiling a ramp up from the top layer crashes into.</summary>
        public const float CeilingY = FieldSpec.CeilingY + 0.02f;

        /// <summary>World z of an object at track position <paramref name="trackZ"/> (the world streams toward the craft).</summary>
        /// <remarks>The subtraction is done in double precision: track positions grow without bound.</remarks>
        public static float WorldZ(double trackZ, double shipDistance)
        {
            return (float)(trackZ - shipDistance);
        }

        /// <summary>Centre (X or Y, metres) of field column/row <paramref name="index"/> (may lie outside the field).</summary>
        public static float CellCenter(int index)
        {
            return FieldSpec.CellCenter(index);
        }

        /// <summary>World position of the centre of field cell (<paramref name="col"/>, <paramref name="row"/>) at world z.</summary>
        public static Vector3 CellPosition(int col, int row, float worldZ)
        {
            return new Vector3(FieldSpec.CellCenter(col), FieldSpec.CellCenter(row), worldZ);
        }

        /// <summary>World position of something at (x, y) and track position <paramref name="trackZ"/>.</summary>
        public static Vector3 Point(float x, float y, double trackZ, double shipDistance)
        {
            return new Vector3(x, y, WorldZ(trackZ, shipDistance));
        }

        /// <summary>World z behind the craft at which a passed obstacle's rear starts to fade (collision is over by then).</summary>
        public const float PassedFadeStart = -0.75f;

        /// <summary>How close (m) to the camera a passed obstacle's front may come before it must be gone.</summary>
        public const float PassedFadeCameraGap = 2f;

        /// <summary>
        /// 1 → 0 fade of an obstacle the craft has passed, so the chase camera never flies through it: starts when its rear
        /// (largest world z, <paramref name="rearZ"/>) is <see cref="PassedFadeStart"/> behind the craft, and reaches 0
        /// before its front (<paramref name="frontZ"/>) gets within <see cref="PassedFadeCameraGap"/> of the camera at
        /// <paramref name="cameraZ"/> (over at least 0.75 m of travel when the camera is close, as in the menu orbit).
        /// </summary>
        public static float PassedFade(float rearZ, float frontZ, float cameraZ)
        {
            if (rearZ >= PassedFadeStart)
            {
                return 1f;
            }

            float length = Mathf.Max(0f, rearZ - frontZ);
            float endRear = Mathf.Min(cameraZ + PassedFadeCameraGap + length, PassedFadeStart - 0.75f);
            float t = Mathf.InverseLerp(endRear, PassedFadeStart, rearZ);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Within this distance (m) of the camera an obstacle switches to the dithered NeonSolid variant, whose shader
        /// dissolves whatever comes closer than ~3.5 m (gone at 0.8 m): the menu orbit camera sits beside the craft, where
        /// obstacles can reach it before <see cref="PassedFade"/> starts.
        /// </summary>
        public const float DitherNearCamera = 4f;

        /// <summary>
        /// 0..1 "warm-up" of an obstacle at world z: 0 where it spawns deep in the fog, rising to 1 by
        /// <paramref name="fullAt"/> metres. Edges glow up as obstacles approach.
        /// </summary>
        public static float WarmUp(float worldZ, float fullAt = 120f)
        {
            float t = Mathf.InverseLerp(fullAt, VisibleLength, worldZ);
            return 1f - t * t * (3f - 2f * t);
        }
    }
}
