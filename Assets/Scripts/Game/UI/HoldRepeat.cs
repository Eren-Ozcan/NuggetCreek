using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Repeats an action while a button is held: the button's own click does the first one,
    /// then after <see cref="Delay"/> this fires every <see cref="Interval"/> until release.
    /// </summary>
    public sealed class HoldRepeat : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float Delay = 0.35f;
        public float Interval = 0.15f;
        public Action Repeat;

        bool held;
        float timer;

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            timer = Delay;
        }

        public void OnPointerUp(PointerEventData eventData) => held = false;

        public void OnPointerExit(PointerEventData eventData) => held = false;

        void OnDisable() => held = false;

        void Update()
        {
            if (!held)
                return;
            timer -= Time.unscaledDeltaTime;
            while (timer <= 0 && held)
            {
                Repeat?.Invoke();
                timer += Interval;
            }
        }
    }
}
