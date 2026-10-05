using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The tutorial's veil (design doc 9.1): a full-screen shade with one clear hole over what
    /// Pete points at. It takes every tap except those inside <see cref="PassRects"/>, which
    /// fall through to the control underneath, so only the cued control can be pressed.
    /// Rects are in this graphic's local space.
    /// </summary>
    public sealed class TutorialDim : MaskableGraphic, ICanvasRaycastFilter
    {
        /// <summary>The clear hole; null shades the whole screen.</summary>
        public Rect? Hole { get; private set; }

        /// <summary>Where a tap reaches the control underneath.</summary>
        public readonly List<Rect> PassRects = new List<Rect>();

        public void SetHole(Rect? hole)
        {
            if (Hole == hole)
                return;
            Hole = hole;
            SetVerticesDirty();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (PassRects.Count == 0)
                return true;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local))
                return true;
            foreach (Rect pass in PassRects)
                if (pass.Contains(local))
                    return false;
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect all = rectTransform.rect;
            if (!(Hole is Rect hole))
            {
                Quad(vh, all.xMin, all.yMin, all.xMax, all.yMax);
                return;
            }
            float left = Mathf.Clamp(hole.xMin, all.xMin, all.xMax);
            float right = Mathf.Clamp(hole.xMax, all.xMin, all.xMax);
            float bottom = Mathf.Clamp(hole.yMin, all.yMin, all.yMax);
            float top = Mathf.Clamp(hole.yMax, all.yMin, all.yMax);
            Quad(vh, all.xMin, top, all.xMax, all.yMax);
            Quad(vh, all.xMin, all.yMin, all.xMax, bottom);
            Quad(vh, all.xMin, bottom, left, top);
            Quad(vh, right, bottom, all.xMax, top);
        }

        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            if (x1 <= x0 || y1 <= y0)
                return;
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), color, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), color, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), color, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
