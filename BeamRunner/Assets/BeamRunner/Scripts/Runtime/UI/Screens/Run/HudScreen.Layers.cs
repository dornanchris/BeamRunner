using BeamRunner.Core;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// The layer gauge: three bars stacked at the left edge, one per layer in its rails' colour (bottom = the level's
    /// secondary, middle = primary, top = accent, as the track draws them), with the craft's layer lit and enlarged; a red
    /// ceiling line above them that throbs while the craft rides the top layer (the ceiling is right overhead) and a dim
    /// void line below. While falling, the layer below blinks (where it may land). No text: nothing to translate.
    /// </summary>
    public sealed partial class HudScreen
    {
        private const float LayerBarWidth = 58f;
        private const float LayerBarHeight = 16f;
        private const float LayerSpacing = 46f;

        private RectTransform _layerRoot;
        private readonly Image[] _layerBars = new Image[FieldSpec.LayerCount];
        private Image _ceilingLine;
        private Image _voidLine;
        private int _layerLevel = -1;

        private void BuildLayers()
        {
            _layerRoot = UIFactory.CreateRect(Rect, "Layers");
            UIFactory.Anchor(_layerRoot, AnchorPreset.MiddleLeft, new Vector2(Margin + 8f, -40f));
            UIFactory.SetSize(_layerRoot, LayerBarWidth + 16f, LayerSpacing * (FieldSpec.LayerCount + 1) + 24f);
            _layerRoot.gameObject.AddComponent<Canvas>(); // redrawn every frame: keep it off the HUD's main canvas
            RunUi.PassiveGroup(_layerRoot);

            for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
            {
                _layerBars[layer] = Bar("Layer" + layer, LayerY(layer), LayerBarWidth, LayerBarHeight, Color.white);
            }

            _ceilingLine = Bar("Ceiling", LayerY(FieldSpec.TopLayer) + LayerSpacing * 0.62f, LayerBarWidth + 14f, 5f, UITheme.Danger);
            _voidLine = Bar("Void", LayerY(0) - LayerSpacing * 0.62f, LayerBarWidth + 14f, 3f, new Color(1f, 1f, 1f, 0.12f));
        }

        private Image Bar(string name, float y, float width, float height, Color color)
        {
            RectTransform r = UIFactory.CreateRect(_layerRoot, name);
            UIFactory.Anchor(r, AnchorPreset.Center, new Vector2(0f, y));
            UIFactory.SetSize(r, width, height);
            return UIFactory.PillImage(r, color, "Pill");
        }

        private static float LayerY(int layer)
        {
            return (layer - FieldSpec.MiddleLayer) * LayerSpacing;
        }

        private void UpdateLayers(RunSimulation run)
        {
            if (_layerRoot == null)
            {
                return;
            }

            if (run.Level != _layerLevel)
            {
                // The level palette, as the rails take it by layer.
                _layerLevel = run.Level;
                ZoneTheme theme = ZoneThemes.ForLevel(run.Level);
                _layerBars[0].color = ToColor(theme.Secondary);
                _layerBars[1].color = ToColor(theme.Primary);
                _layerBars[2].color = ToColor(theme.Accent);
            }

            int layer = run.IsClimbing ? FieldSpec.MiddleLayer : Mathf.Clamp(run.Layer, 0, FieldSpec.TopLayer);
            int landing = run.IsFalling ? layer - 1 : -1;
            bool open = run.LevelSpec.TopLayer;
            float blink = 0.5f + 0.5f * Mathf.Sin(_time * 14f);
            for (int i = 0; i < FieldSpec.LayerCount; i++)
            {
                bool here = i == layer && !run.IsFalling;
                float alpha = here ? 1f : i == landing ? 0.3f + 0.6f * blink : (i == FieldSpec.TopLayer && !open ? 0.06f : 0.2f);
                _layerBars[i].canvasRenderer.SetAlpha(alpha);
                float scale = here ? 1.25f : 1f;
                _layerBars[i].rectTransform.parent.localScale = new Vector3(scale, here ? 1.35f : 1f, 1f);
            }

            bool underCeiling = layer == FieldSpec.TopLayer && !run.IsFalling;
            float throb = UITheme.ReducedMotion ? 1f : 0.6f + 0.4f * Mathf.Sin(_time * 7f);
            _ceilingLine.canvasRenderer.SetAlpha(underCeiling ? throb : 0.3f);
        }

        private static Color ToColor(ColorRGB c)
        {
            return new Color(c.R, c.G, c.B, 1f);
        }
    }
}
