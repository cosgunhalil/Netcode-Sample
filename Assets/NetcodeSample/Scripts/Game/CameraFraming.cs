using DPF.Unity;
using NetcodeSample.Simulation;
using UnityEngine;

namespace NetcodeSample.Game
{
    public static class CameraFraming
    {
        /// <summary>Points the main camera down at the level so both bases are in view.</summary>
        public static void FrameBases(LevelData level)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 red = level.GetBasePosition(Team.Red).ToVector3();
            Vector3 blue = level.GetBasePosition(Team.Blue).ToVector3();
            Vector3 center = (red + blue) * 0.5f;
            float span = Vector3.Distance(red, blue);
            camera.transform.position = center + new Vector3(0f, span * 0.9f, -span * 0.35f);
            camera.transform.LookAt(center);
        }
    }
}
