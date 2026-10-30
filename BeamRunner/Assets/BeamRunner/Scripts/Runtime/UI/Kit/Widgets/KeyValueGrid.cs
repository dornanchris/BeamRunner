using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Grid of stat cells (small muted caption over a big value), filled row by row with <see cref="Add"/>.
    /// Build with <see cref="UIFactory.KeyValueGrid"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyValueGrid : MonoBehaviour
    {
        private readonly List<Text> _values = new List<Text>();
        private int _columns = 2;
        private RectTransform _currentRow;
        private int _inRow;
        private int _valueSize;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public int Count
        {
            get { return _values.Count; }
        }

        /// <summary>Value text of the i-th cell.</summary>
        public Text ValueAt(int index)
        {
            return _values[index];
        }

        /// <summary>Adds a cell and returns its value text.</summary>
        public Text Add(string key, string value, Color? valueColor = null)
        {
            if (_currentRow == null || _inRow >= _columns)
            {
                _currentRow = UIFactory.CreateRect(transform, "Row");
                HorizontalLayoutGroup h = UIFactory.HStack(_currentRow, UITheme.SpaceMd, null, TextAnchor.UpperLeft, true);
                h.childForceExpandWidth = true;
                _inRow = 0;
            }

            RectTransform cell = UIFactory.CreateRect(_currentRow, "Cell");
            UIFactory.VStack(cell, 0f, null, TextAnchor.UpperLeft, true);
            UIFactory.SetLayout(cell, 0f, -1f, 1f);

            // Captions are shown as given (the string tables store them upper-case; no runtime case changes).
            Text k = UIFactory.Label(cell, key ?? string.Empty, UITheme.TextCaption, UITheme.TextMuted, TextAnchor.LowerLeft, UIFont.Bold);
            Text v = UIFactory.LabelLine(cell, value ?? string.Empty, _valueSize, valueColor ?? UITheme.TextPrimary, TextAnchor.UpperLeft, UIFont.Title, TextGlow.Shadow);
            v.horizontalOverflow = HorizontalWrapMode.Wrap;
            _values.Add(v);
            _inRow++;
            return v;
        }

        /// <summary>Removes every cell.</summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _values.Clear();
            _currentRow = null;
            _inRow = 0;
        }

        internal void Build(int columns, int valueSize)
        {
            _columns = Mathf.Max(1, columns);
            _valueSize = valueSize;
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(900f, 200f);
            UIFactory.VStack(root, UITheme.SpaceLg, null, TextAnchor.UpperLeft, true);
        }
    }
}
