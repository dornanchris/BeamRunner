using System.Collections.Generic;
using BeamRunner.Core;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>Layout data for a procedural ship design (local space: nose +Z, up +Y, metres).</summary>
    public sealed class ShipDesignInfo
    {
        public readonly string Design;

        /// <summary>Nose-to-tail length.</summary>
        public readonly float Length;

        /// <summary>Wing span.</summary>
        public readonly float Span;

        /// <summary>Engine exhaust positions (engine glows and flares attach here).</summary>
        public readonly Vector3[] Engines;

        /// <summary>Glow sprite size per engine.</summary>
        public readonly float EngineSize;

        /// <summary>Lateral offsets (m, +x = right) of the ribbon trails left behind the ship.</summary>
        public readonly float[] TrailOffsets;

        /// <summary>Width of each trail ribbon at the ship.</summary>
        public readonly float TrailWidth;

        public ShipDesignInfo(string design, float length, float span, Vector3[] engines, float engineSize, float[] trailOffsets, float trailWidth)
        {
            Design = design;
            Length = length;
            Span = span;
            Engines = engines;
            EngineSize = engineSize;
            TrailOffsets = trailOffsets;
            TrailWidth = trailWidth;
        }

        /// <summary>Z of the rearmost engine (where trails start).</summary>
        public float TailZ
        {
            get
            {
                float z = 0f;
                for (int i = 0; i < Engines.Length; i++)
                {
                    z = Mathf.Min(z, Engines[i].z);
                }

                return z;
            }
        }
    }

    public static partial class MeshFactory
    {
        /// <summary>Procedural ship design ids (the shuttle uses the legacy model when available).</summary>
        public static readonly string[] ShipDesigns = { "arrow", "manta", "viper", "halo", "phantom", "nova", "shuttle" };

        private static readonly Dictionary<string, ShipDesignInfo> ShipInfos = new Dictionary<string, ShipDesignInfo>
        {
            { "arrow", new ShipDesignInfo("arrow", 1.45f, 1.44f, new[] { new Vector3(-0.13f, -0.01f, -0.69f), new Vector3(0.13f, -0.01f, -0.69f) }, 0.3f, new[] { -0.13f, 0.13f }, 0.16f) },
            { "manta", new ShipDesignInfo("manta", 1.2f, 1.56f, new[] { new Vector3(-0.18f, 0.01f, -0.58f), new Vector3(0.18f, 0.01f, -0.58f) }, 0.3f, new[] { -0.18f, 0.18f }, 0.18f) },
            { "viper", new ShipDesignInfo("viper", 1.55f, 1.06f, new[] { new Vector3(0f, 0.035f, -0.72f) }, 0.4f, new[] { 0f }, 0.3f) },
            { "halo", new ShipDesignInfo("halo", 1.25f, 1.08f, new[] { new Vector3(0f, 0.035f, -0.62f) }, 0.36f, new[] { 0f }, 0.28f) },
            { "phantom", new ShipDesignInfo("phantom", 1.4f, 1.44f, new[] { new Vector3(-0.12f, 0.03f, -0.4f), new Vector3(0.12f, 0.03f, -0.4f) }, 0.26f, new[] { -0.12f, 0.12f }, 0.14f) },
            { "nova", new ShipDesignInfo("nova", 1.45f, 1.32f, new[] { new Vector3(0f, 0.04f, -0.68f) }, 0.48f, new[] { 0f }, 0.36f) },
            { "shuttle", new ShipDesignInfo("shuttle", 1.5f, 1.2f, new[] { new Vector3(0f, 0.12f, -0.76f), new Vector3(-0.08f, 0.0f, -0.76f), new Vector3(0.08f, 0.0f, -0.76f) }, 0.26f, new[] { 0f }, 0.3f) },
        };

        // Unit cross-sections for lofts (x right, y up).
        private static readonly Vector2[] Hex =
        {
            new Vector2(0f, 1f), new Vector2(0.866f, 0.5f), new Vector2(0.866f, -0.5f),
            new Vector2(0f, -1f), new Vector2(-0.866f, -0.5f), new Vector2(-0.866f, 0.5f),
        };

        private static readonly Vector2[] Keel =
        {
            new Vector2(0f, 1f), new Vector2(0.72f, 0.66f), new Vector2(1f, 0f), new Vector2(0.72f, -0.6f),
            new Vector2(0f, -0.82f), new Vector2(-0.72f, -0.6f), new Vector2(-1f, 0f), new Vector2(-0.72f, 0.66f),
        };

        private static readonly Vector2[] Oct =
        {
            new Vector2(0.383f, 0.924f), new Vector2(0.924f, 0.383f), new Vector2(0.924f, -0.383f), new Vector2(0.383f, -0.924f),
            new Vector2(-0.383f, -0.924f), new Vector2(-0.924f, -0.383f), new Vector2(-0.924f, 0.383f), new Vector2(-0.383f, 0.924f),
        };

        private static readonly Vector2[] Diamond =
        {
            new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f),
        };

        private struct Section
        {
            public readonly float Z;
            public readonly float Cx;
            public readonly float Cy;
            public readonly float Sx;
            public readonly float Sy;

            public Section(float z, float cx, float cy, float sx, float sy)
            {
                Z = z;
                Cx = cx;
                Cy = cy;
                Sx = sx;
                Sy = sy;
            }

            public bool IsPoint
            {
                get { return Sx <= 1e-6f && Sy <= 1e-6f; }
            }

            public Vector3 Centre
            {
                get { return new Vector3(Cx, Cy, Z); }
            }

            public Vector3 Point(Vector2 unit)
            {
                return new Vector3(Cx + unit.x * Sx, Cy + unit.y * Sy, Z);
            }
        }

        /// <summary>Layout info for a design (engines, trails, size). Unknown ids fall back to "arrow".</summary>
        public static ShipDesignInfo ShipInfo(string design)
        {
            ShipDesignInfo info;
            if (design != null && ShipInfos.TryGetValue(design, out info))
            {
                return info;
            }

            return ShipInfos["arrow"];
        }

        /// <summary>
        /// Low-poly, flat-shaded ship hull (~1.4 m long, nose +Z, up +Y) whose neon edges trace the silhouette.
        /// Designs: arrow, manta, viper, halo, phantom, nova, shuttle (procedural stand-in). Unknown → arrow.
        /// </summary>
        public static Mesh Ship(string design)
        {
            string id = design != null && ShipInfos.ContainsKey(design) ? design : "arrow";
            string key = "ship:" + id;
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            switch (id)
            {
                case "manta":
                    BuildManta(Builder);
                    break;
                case "viper":
                    BuildViper(Builder);
                    break;
                case "halo":
                    BuildHalo(Builder);
                    break;
                case "phantom":
                    BuildPhantom(Builder);
                    break;
                case "nova":
                    BuildNova(Builder);
                    break;
                case "shuttle":
                    BuildShuttle(Builder);
                    break;
                default:
                    BuildArrow(Builder);
                    break;
            }

            return Store(key, Builder.ToMesh("BeamRunner Ship " + id));
        }

        // ------------------------------------------------------------------ designs

        private static void BuildArrow(MeshBuilder b)
        {
            Loft(b, Keel, new[]
            {
                new Section(0.78f, 0f, 0f, 0f, 0f),
                new Section(0.42f, 0f, 0.02f, 0.1f, 0.075f),
                new Section(-0.05f, 0f, 0.04f, 0.17f, 0.11f),
                new Section(-0.5f, 0f, 0.035f, 0.15f, 0.1f),
                new Section(-0.62f, 0f, 0.03f, 0.12f, 0.08f),
            });
            Loft(b, Hex, new[]
            {
                new Section(0.32f, 0f, 0.09f, 0f, 0f),
                new Section(0.12f, 0f, 0.125f, 0.06f, 0.045f),
                new Section(-0.14f, 0f, 0.13f, 0.065f, 0.04f),
                new Section(-0.3f, 0f, 0.11f, 0f, 0f),
            });
            MirrorPair(b, delegate(MeshBuilder m)
            {
                // Delta wing.
                Plate(m, new[] { new Vector3(0.08f, -0.005f, 0.2f), new Vector3(0.7f, -0.005f, -0.44f), new Vector3(0.72f, -0.005f, -0.54f), new Vector3(0.1f, -0.005f, -0.56f) },
                    Vector3.up, 0.04f, 0.18f);

                // Wingtip fin.
                Plate(m, new[] { new Vector3(0.71f, 0f, -0.38f), new Vector3(0.71f, 0.15f, -0.5f), new Vector3(0.71f, 0.15f, -0.58f), new Vector3(0.71f, 0f, -0.56f) },
                    Vector3.right, 0.022f, 0.15f);

                // Engine pod.
                Loft(m, Oct, new[]
                {
                    new Section(-0.2f, 0.13f, -0.01f, 0.045f, 0.045f),
                    new Section(-0.58f, 0.13f, -0.01f, 0.065f, 0.065f),
                    new Section(-0.68f, 0.13f, -0.01f, 0.055f, 0.055f),
                });
            });

            // Dorsal fin.
            Plate(b, new[] { new Vector3(0f, 0.1f, -0.18f), new Vector3(0f, 0.33f, -0.52f), new Vector3(0f, 0.33f, -0.62f), new Vector3(0f, 0.1f, -0.6f) },
                Vector3.right, 0.03f, 0.15f);
        }

        private static void BuildManta(MeshBuilder b)
        {
            // Broad convex wing-body.
            Plate(b, new[]
            {
                new Vector3(0f, 0f, 0.6f), new Vector3(0.22f, 0f, 0.4f), new Vector3(0.78f, 0f, -0.16f), new Vector3(0.64f, 0f, -0.4f),
                new Vector3(0.2f, 0f, -0.56f), new Vector3(-0.2f, 0f, -0.56f), new Vector3(-0.64f, 0f, -0.4f), new Vector3(-0.78f, 0f, -0.16f),
                new Vector3(-0.22f, 0f, 0.4f),
            }, Vector3.up, 0.07f, 0.12f);

            // Central hump / cockpit.
            Loft(b, Hex, new[]
            {
                new Section(0.46f, 0f, 0.03f, 0f, 0f),
                new Section(0.22f, 0f, 0.06f, 0.14f, 0.07f),
                new Section(-0.2f, 0f, 0.065f, 0.17f, 0.075f),
                new Section(-0.5f, 0f, 0.04f, 0.1f, 0.04f),
            });
            Loft(b, Hex, new[]
            {
                new Section(0.28f, 0f, 0.1f, 0f, 0f),
                new Section(0.12f, 0f, 0.125f, 0.055f, 0.04f),
                new Section(-0.1f, 0f, 0.13f, 0.06f, 0.035f),
                new Section(-0.24f, 0f, 0.11f, 0f, 0f),
            });

            MirrorPair(b, delegate(MeshBuilder m)
            {
                // Canted tail fins.
                Fin(m, new Vector3(0.4f, 0.02f, 0f), -0.42f, new[]
                {
                    new Vector2(0f, -0.26f), new Vector2(0.17f, -0.4f), new Vector2(0.17f, -0.47f), new Vector2(0f, -0.46f),
                }, 0.022f);

                // Engine nacelles under the trailing edge.
                Loft(m, Oct, new[]
                {
                    new Section(-0.2f, 0.18f, 0.0f, 0.05f, 0.04f),
                    new Section(-0.5f, 0.18f, 0.0f, 0.065f, 0.05f),
                    new Section(-0.58f, 0.18f, 0.0f, 0.055f, 0.042f),
                });
            });
        }

        private static void BuildViper(MeshBuilder b)
        {
            Loft(b, Oct, new[]
            {
                new Section(0.85f, 0f, 0.01f, 0f, 0f),
                new Section(0.45f, 0f, 0.02f, 0.055f, 0.05f),
                new Section(0f, 0f, 0.035f, 0.1f, 0.085f),
                new Section(-0.52f, 0f, 0.035f, 0.12f, 0.09f),
                new Section(-0.7f, 0f, 0.035f, 0.085f, 0.07f),
            });
            Loft(b, Hex, new[]
            {
                new Section(0.3f, 0f, 0.07f, 0f, 0f),
                new Section(0.12f, 0f, 0.1f, 0.045f, 0.04f),
                new Section(-0.1f, 0f, 0.105f, 0.05f, 0.035f),
                new Section(-0.25f, 0f, 0.09f, 0f, 0f),
            });
            MirrorPair(b, delegate(MeshBuilder m)
            {
                // Canards.
                Plate(m, new[] { new Vector3(0.04f, 0.015f, 0.44f), new Vector3(0.21f, 0.015f, 0.3f), new Vector3(0.21f, 0.015f, 0.25f), new Vector3(0.05f, 0.015f, 0.27f) },
                    Vector3.up, 0.022f, 0.15f);

                // Swept main wings.
                Plate(m, new[] { new Vector3(0.08f, 0f, -0.05f), new Vector3(0.53f, 0f, -0.46f), new Vector3(0.53f, 0f, -0.56f), new Vector3(0.1f, 0f, -0.58f) },
                    Vector3.up, 0.035f, 0.18f);

                // Twin canted fins.
                Fin(m, new Vector3(0.1f, 0.06f, 0f), -0.3f, new[]
                {
                    new Vector2(0f, -0.25f), new Vector2(0.26f, -0.5f), new Vector2(0.26f, -0.6f), new Vector2(0f, -0.62f),
                }, 0.022f);
            });

            // Single big engine bell.
            Loft(b, Oct, new[]
            {
                new Section(-0.6f, 0f, 0.035f, 0.08f, 0.08f),
                new Section(-0.73f, 0f, 0.035f, 0.1f, 0.1f),
            });
        }

        private static void BuildHalo(MeshBuilder b)
        {
            Loft(b, Oct, new[]
            {
                new Section(0.62f, 0f, 0.02f, 0f, 0f),
                new Section(0.32f, 0f, 0.03f, 0.09f, 0.08f),
                new Section(-0.15f, 0f, 0.04f, 0.12f, 0.11f),
                new Section(-0.5f, 0f, 0.035f, 0.1f, 0.09f),
                new Section(-0.6f, 0f, 0.035f, 0.075f, 0.068f),
            });
            Loft(b, Hex, new[]
            {
                new Section(0.36f, 0f, 0.08f, 0f, 0f),
                new Section(0.18f, 0f, 0.108f, 0.045f, 0.035f),
                new Section(0f, 0f, 0.115f, 0.05f, 0.03f),
                new Section(-0.12f, 0f, 0.1f, 0f, 0f),
            });

            // The ring wing: ten faceted segments around the pod.
            const int segments = 10;
            b.SetTransform(new Vector3(0f, 0.035f, 0f));
            for (int k = 0; k < segments; k++)
            {
                float a = MathUtil.TwoPi * k / segments;
                BuildArcBlock(b, a, MathUtil.TwoPi / segments, 0.47f, 0.53f, -0.48f, 0.3f, 1f, true);
            }

            // Three struts joining the ring to the pod (top, lower left, lower right).
            for (int k = 0; k < 3; k++)
            {
                float a = MathUtil.Pi + MathUtil.TwoPi * k / 3f;
                Vector3 axis = RadialRight(a);
                Plate(b, new[]
                {
                    RadialPoint(a, 0.07f, -0.42f), RadialPoint(a, 0.48f, -0.46f),
                    RadialPoint(a, 0.48f, -0.24f), RadialPoint(a, 0.07f, -0.16f),
                }, axis, 0.028f, 0.1f);
            }

            b.ResetTransform();
        }

        private static void BuildPhantom(MeshBuilder b)
        {
            // Faceted stealth wing: a low pyramid over a sawtooth planform.
            var outline = new[]
            {
                new Vector3(0f, 0f, 0.7f), new Vector3(0.3f, 0f, 0.14f), new Vector3(0.72f, 0f, -0.36f), new Vector3(0.52f, 0f, -0.46f),
                new Vector3(0.33f, 0f, -0.3f), new Vector3(0.16f, 0f, -0.48f), new Vector3(0f, 0f, -0.34f), new Vector3(-0.16f, 0f, -0.48f),
                new Vector3(-0.33f, 0f, -0.3f), new Vector3(-0.52f, 0f, -0.46f), new Vector3(-0.72f, 0f, -0.36f), new Vector3(-0.3f, 0f, 0.14f),
            };
            var top = new Vector3(0f, 0.15f, -0.04f);
            var bottom = new Vector3(0f, -0.055f, -0.04f);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 p = outline[i];
                Vector3 q = outline[(i + 1) % outline.Length];
                b.AddTriangle(p, q, top, Vector3.up);
                b.AddTriangle(q, p, bottom, Vector3.down);
            }

            Loft(b, Diamond, new[]
            {
                new Section(0.42f, 0f, 0.05f, 0f, 0f),
                new Section(0.25f, 0f, 0.085f, 0.05f, 0.035f),
                new Section(0.06f, 0f, 0.11f, 0.045f, 0.03f),
                new Section(-0.04f, 0f, 0.12f, 0f, 0f),
            });

            MirrorPair(b, delegate(MeshBuilder m)
            {
                // V-tail.
                Fin(m, new Vector3(0.2f, 0.06f, 0f), -0.66f, new[]
                {
                    new Vector2(0f, -0.14f), new Vector2(0.17f, -0.3f), new Vector2(0.17f, -0.37f), new Vector2(0f, -0.36f),
                }, 0.02f);

                // Recessed exhaust slots.
                Loft(m, Diamond, new[]
                {
                    new Section(-0.2f, 0.12f, 0.04f, 0.06f, 0.025f),
                    new Section(-0.41f, 0.12f, 0.03f, 0.075f, 0.022f),
                });
            });
        }

        private static void BuildNova(MeshBuilder b)
        {
            Loft(b, Oct, new[]
            {
                new Section(0.72f, 0f, 0.02f, 0f, 0f),
                new Section(0.38f, 0f, 0.03f, 0.085f, 0.075f),
                new Section(-0.15f, 0f, 0.04f, 0.15f, 0.12f),
                new Section(-0.52f, 0f, 0.04f, 0.17f, 0.14f),
                new Section(-0.6f, 0f, 0.04f, 0.13f, 0.11f),
            });
            Loft(b, Hex, new[]
            {
                new Section(0.4f, 0f, 0.07f, 0f, 0f),
                new Section(0.2f, 0f, 0.105f, 0.05f, 0.04f),
                new Section(-0.05f, 0f, 0.13f, 0.055f, 0.04f),
                new Section(-0.2f, 0f, 0.12f, 0f, 0f),
            });
            MirrorPair(b, delegate(MeshBuilder m)
            {
                // Forward-swept wings.
                Plate(m, new[] { new Vector3(0.1f, 0.01f, -0.44f), new Vector3(0.1f, 0.01f, -0.16f), new Vector3(0.62f, 0.01f, 0.12f), new Vector3(0.66f, 0.01f, 0.02f) },
                    Vector3.up, 0.04f, 0.18f);
            });

            // Three flare spikes radiating back from the engine (top, lower left, lower right).
            for (int k = 0; k < 3; k++)
            {
                float a = MathUtil.Pi + MathUtil.TwoPi * k / 3f;
                Vector3 baseCentre = RadialPoint(a, 0.13f, -0.38f) + new Vector3(0f, 0.04f, 0f);
                Vector3 tip = RadialPoint(a, 0.36f, -0.8f) + new Vector3(0f, 0.04f, 0f);
                Vector3 side = RadialRight(a) * 0.035f;
                Vector3 up = RadialInward(a) * -0.03f;
                Vector3 p0 = baseCentre + side, p1 = baseCentre + up, p2 = baseCentre - side, p3 = baseCentre - up;
                Vector3 c = (baseCentre * 3f + tip) * 0.25f;
                b.AddTriangle(p0, p1, tip, (p0 + p1 + tip) / 3f - c);
                b.AddTriangle(p1, p2, tip, (p1 + p2 + tip) / 3f - c);
                b.AddTriangle(p2, p3, tip, (p2 + p3 + tip) / 3f - c);
                b.AddTriangle(p3, p0, tip, (p3 + p0 + tip) / 3f - c);
            }

            // Engine ring.
            b.SetTransform(new Vector3(0f, 0.04f, 0f));
            BuildArcBlock(b, 0f, MathUtil.TwoPi, 0.1f, 0.165f, -0.68f, 0.09f, 2f, true);
            b.ResetTransform();
        }

        private static void BuildShuttle(MeshBuilder b)
        {
            Loft(b, Keel, new[]
            {
                new Section(0.75f, 0f, -0.01f, 0.035f, 0.03f),
                new Section(0.62f, 0f, 0f, 0.1f, 0.085f),
                new Section(0.3f, 0f, 0.02f, 0.14f, 0.12f),
                new Section(-0.55f, 0f, 0.02f, 0.14f, 0.12f),
                new Section(-0.66f, 0f, 0.02f, 0.13f, 0.11f),
            }, true, true);
            MirrorPair(b, delegate(MeshBuilder m)
            {
                Plate(m, new[] { new Vector3(0.1f, -0.07f, 0.22f), new Vector3(0.6f, -0.07f, -0.48f), new Vector3(0.6f, -0.07f, -0.6f), new Vector3(0.1f, -0.07f, -0.62f) },
                    Vector3.up, 0.035f, 0.15f);
                Loft(m, Oct, new[]
                {
                    new Section(-0.62f, 0.08f, 0f, 0.035f, 0.035f),
                    new Section(-0.76f, 0.08f, 0f, 0.05f, 0.05f),
                });
            });
            Plate(b, new[] { new Vector3(0f, 0.12f, -0.22f), new Vector3(0f, 0.46f, -0.54f), new Vector3(0f, 0.46f, -0.64f), new Vector3(0f, 0.12f, -0.64f) },
                Vector3.right, 0.03f, 0.12f);
            Loft(b, Oct, new[]
            {
                new Section(-0.62f, 0f, 0.12f, 0.04f, 0.04f),
                new Section(-0.76f, 0f, 0.12f, 0.058f, 0.058f),
            });
        }

        // ------------------------------------------------------------------ part helpers

        private delegate void PartBuilder(MeshBuilder b);

        /// <summary>Builds a part on the right (+x) side and again mirrored on the left.</summary>
        private static void MirrorPair(MeshBuilder b, PartBuilder part)
        {
            b.ResetTransform();
            part(b);
            b.SetMirrorX(Vector3.zero);
            part(b);
            b.ResetTransform();
        }

        /// <summary>
        /// Lofts a unit cross-section through sections (any order along z). Point sections become cones;
        /// ends are capped unless they are points.
        /// </summary>
        private static void Loft(MeshBuilder b, Vector2[] shape, Section[] sections, bool capFirst = true, bool capLast = true)
        {
            int n = shape.Length;
            for (int k = 0; k < sections.Length - 1; k++)
            {
                Section s0 = sections[k];
                Section s1 = sections[k + 1];
                Vector3 axisMid = (s0.Centre + s1.Centre) * 0.5f;
                for (int j = 0; j < n; j++)
                {
                    int j1 = (j + 1) % n;
                    Vector3 a0 = s0.Point(shape[j]);
                    Vector3 a1 = s0.Point(shape[j1]);
                    Vector3 b0 = s1.Point(shape[j]);
                    Vector3 b1 = s1.Point(shape[j1]);
                    if (s0.IsPoint)
                    {
                        b.AddTriangle(a0, b1, b0, (a0 + b0 + b1) / 3f - axisMid);
                    }
                    else if (s1.IsPoint)
                    {
                        b.AddTriangle(a0, a1, b0, (a0 + a1 + b0) / 3f - axisMid);
                    }
                    else
                    {
                        b.AddQuad(a0, a1, b1, b0, (a0 + a1 + b0 + b1) * 0.25f - axisMid);
                    }
                }
            }

            if (capFirst && !sections[0].IsPoint && sections.Length > 1)
            {
                AddCap(b, shape, sections[0], sections[0].Centre - sections[1].Centre);
            }

            int last = sections.Length - 1;
            if (capLast && !sections[last].IsPoint && sections.Length > 1)
            {
                AddCap(b, shape, sections[last], sections[last].Centre - sections[last - 1].Centre);
            }
        }

        private static void AddCap(MeshBuilder b, Vector2[] shape, Section s, Vector3 outward)
        {
            var pts = new Vector3[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                pts[i] = s.Point(shape[i]);
            }

            b.AddPolygon(pts, outward);
        }

        /// <summary>
        /// Thin plate from a convex outline with bevelled edges: the outline is the sharp mid ridge, the top
        /// and bottom faces are the outline shrunk toward its centroid and offset along the thickness axis.
        /// </summary>
        private static void Plate(MeshBuilder b, Vector3[] outline, Vector3 axis, float thickness, float bevel)
        {
            int n = outline.Length;
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                centroid += outline[i];
            }

            centroid /= n;
            axis = axis.normalized;
            Vector3 up = axis * (thickness * 0.5f);
            var top = new Vector3[n];
            var bottom = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 inset = centroid + (outline[i] - centroid) * (1f - bevel);
                top[i] = inset + up;
                bottom[i] = inset - up;
            }

            b.AddPolygon(top, axis);
            b.AddPolygon(bottom, -axis);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 edgeOut = (outline[i] + outline[j]) * 0.5f - centroid;
                edgeOut -= axis * Vector3.Dot(edgeOut, axis);
                edgeOut.Normalize();
                if (bevel > 0f)
                {
                    b.AddQuad(outline[i], outline[j], top[j], top[i], edgeOut + axis * 0.5f);
                    b.AddQuad(outline[i], outline[j], bottom[j], bottom[i], edgeOut - axis * 0.5f);
                }
                else
                {
                    b.AddQuad(bottom[i], bottom[j], top[j], top[i], edgeOut);
                }
            }
        }

        /// <summary>
        /// Vertical fin rooted at <paramref name="root"/>: profile points are (height, z); the fin is tilted
        /// about the Z axis by <paramref name="cant"/> radians (negative leans it outward to the right).
        /// </summary>
        private static void Fin(MeshBuilder b, Vector3 root, float cant, Vector2[] profile, float thickness)
        {
            Vector3 finUp = new Vector3(-Mathf.Sin(cant), Mathf.Cos(cant), 0f);
            Vector3 finSide = new Vector3(Mathf.Cos(cant), Mathf.Sin(cant), 0f);
            var outline = new Vector3[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                outline[i] = root + finUp * profile[i].x + Vector3.forward * profile[i].y;
            }

            Plate(b, outline, finSide, thickness, 0.12f);
        }

        // Polar helpers in the XY plane: angle 0 points down (−Y), increasing toward +X.
        private static Vector3 RadialPoint(float angle, float radius, float z)
        {
            return new Vector3(radius * Mathf.Sin(angle), -radius * Mathf.Cos(angle), z);
        }

        // Unit normal pointing from the radius toward the centre.
        private static Vector3 RadialInward(float angle)
        {
            return new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }

        // Unit tangent toward increasing angle.
        private static Vector3 RadialRight(float angle)
        {
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        }

        /// <summary>
        /// Appends an annular-sector block (around the local Z axis, angle 0 at −Y, increasing toward +X) to a builder,
        /// extruded from z0 to z0 + depth. A full circle (width ≥ 2π) becomes a closed ring with no side faces. Used by
        /// the ring-winged "halo" hull.
        /// </summary>
        private static void BuildArcBlock(MeshBuilder b, float startAngle, float width, float innerRadius, float outerRadius,
            float z0, float depth, float segmentsPerRadian, bool outerFace)
        {
            bool closed = width >= MathUtil.TwoPi - 1e-4f;
            if (closed)
            {
                width = MathUtil.TwoPi;
            }

            if (width <= 1e-5f || depth <= 1e-5f || outerRadius <= innerRadius)
            {
                return;
            }

            int segments = Mathf.Max(1, Mathf.CeilToInt(width * Mathf.Max(0.5f, segmentsPerRadian)));
            float z1 = z0 + depth;
            float end = startAngle + width;
            float radial = outerRadius - innerRadius;
            const float No = MeshBuilder.NoEdge;
            Vector3 back = Vector3.forward;
            Vector3 front = Vector3.back;

            for (int k = 0; k < segments; k++)
            {
                float a0 = startAngle + width * k / segments;
                float a1 = startAngle + width * (k + 1) / segments;
                float u0 = (float)k / segments;
                float u1 = (float)(k + 1) / segments;

                // Distances (m) along the arc to the start and end side faces; not edges on a closed ring.
                float s0i = closed ? No : (a0 - startAngle) * innerRadius;
                float s1i = closed ? No : (a1 - startAngle) * innerRadius;
                float s0o = closed ? No : (a0 - startAngle) * outerRadius;
                float s1o = closed ? No : (a1 - startAngle) * outerRadius;
                float t0i = closed ? No : (end - a0) * innerRadius;
                float t1i = closed ? No : (end - a1) * innerRadius;
                float t0o = closed ? No : (end - a0) * outerRadius;
                float t1o = closed ? No : (end - a1) * outerRadius;

                Vector3 i0f = RadialPoint(a0, innerRadius, z0);
                Vector3 i1f = RadialPoint(a1, innerRadius, z0);
                Vector3 o0f = RadialPoint(a0, outerRadius, z0);
                Vector3 o1f = RadialPoint(a1, outerRadius, z0);
                Vector3 i0b = RadialPoint(a0, innerRadius, z1);
                Vector3 i1b = RadialPoint(a1, innerRadius, z1);
                Vector3 o0b = RadialPoint(a0, outerRadius, z1);
                Vector3 o1b = RadialPoint(a1, outerRadius, z1);

                // Front (facing the ship / camera) and back faces: u along the arc, v from inner to outer.
                b.AddQuadRaw(i0f, i1f, o1f, o0f, front, front, front, front,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f),
                    new Vector4(s0i, t0i, 0f, radial), new Vector4(s1i, t1i, 0f, radial),
                    new Vector4(s1o, t1o, radial, 0f), new Vector4(s0o, t0o, radial, 0f), front);
                b.AddQuadRaw(i0b, i1b, o1b, o0b, back, back, back, back,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f),
                    new Vector4(s0i, t0i, 0f, radial), new Vector4(s1i, t1i, 0f, radial),
                    new Vector4(s1o, t1o, radial, 0f), new Vector4(s0o, t0o, radial, 0f), back);

                // Inner face (toward the axis): u along the arc, v along z. Smooth radial normals.
                Vector3 n0 = RadialInward(a0);
                Vector3 n1 = RadialInward(a1);
                Vector3 nMid = RadialInward((a0 + a1) * 0.5f);
                b.AddQuadRaw(i0f, i1f, i1b, i0b, n0, n1, n1, n0,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f),
                    new Vector4(s0i, t0i, 0f, depth), new Vector4(s1i, t1i, 0f, depth),
                    new Vector4(s1i, t1i, depth, 0f), new Vector4(s0i, t0i, depth, 0f), nMid);

                if (outerFace)
                {
                    b.AddQuadRaw(o0f, o1f, o1b, o0b, -n0, -n1, -n1, -n0,
                        new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f),
                        new Vector4(s0o, t0o, 0f, depth), new Vector4(s1o, t1o, 0f, depth),
                        new Vector4(s1o, t1o, depth, 0f), new Vector4(s0o, t0o, depth, 0f), -nMid);
                }
            }

            if (!closed)
            {
                b.AddQuad(
                    RadialPoint(startAngle, innerRadius, z0), RadialPoint(startAngle, outerRadius, z0),
                    RadialPoint(startAngle, outerRadius, z1), RadialPoint(startAngle, innerRadius, z1),
                    -RadialRight(startAngle));
                b.AddQuad(
                    RadialPoint(end, innerRadius, z0), RadialPoint(end, outerRadius, z0),
                    RadialPoint(end, outerRadius, z1), RadialPoint(end, innerRadius, z1),
                    RadialRight(end));
            }
        }
    }
}
