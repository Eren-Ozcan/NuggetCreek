using UnityEngine;
using UnityEngine.InputSystem;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The finger (or the mouse in the editor) that swipes the creek and the boulder. Reads the
    /// Input System's current pointer: the primary touch on a phone, the left button on a desktop.
    /// </summary>
    static class PointerInput
    {
        /// <summary>Screen position while the pointer is down; null when it is up or there is none.</summary>
        public static Vector2? Pressed()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || !pointer.press.isPressed)
                return null;
            return pointer.position.ReadValue();
        }
    }
}
