using UnityEngine;

namespace Sporeborn.Runtime
{
    internal static class WorkshopGui
    {
        public static float Scale => Mathf.Clamp(Mathf.Min(Screen.height / 900f, Screen.width / 760f), 0.65f, 2.5f);
    }
}
