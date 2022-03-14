#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace PointCloud.Player
{
    [CustomEditor(typeof(CloudPlayer))]
    public class CloudPlayerEditor : Editor
    {
        private int setFrame;
        private CloudPlayer script;

        private bool inLoad;
        private float progress;

        private void OnEnable()
        {
            script = (CloudPlayer) target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            #region Line 0

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Init"))
            {
                script.Init();
            }
            if (GUILayout.Button("Dispose"))
            {
                script.Dispose();
            }
            if (GUILayout.Button("Clear manager"))
            {
                VideoManager.VPC_ClearPlayer();
            }

            EditorGUILayout.EndHorizontal();

            #endregion

            #region Line 1

            EditorGUILayout.BeginHorizontal();
            
            if (script.playing)
            {
                if (GUILayout.Button("Pause"))
                {
                    script.Pause();
                }
            }
            else if (GUILayout.Button("Play"))
            {
                script.Play();
            }
            if (GUILayout.Button("Stop"))
            {
                script.Stop();
            }

            EditorGUILayout.EndHorizontal();

            #endregion

            #region Line 2

           // EditorGUILayout.BeginHorizontal();

            /*string[] list = VideoManager.GetBuiltInList();
            if (list != null && list.Length > 0)
                EditorGUILayout.Popup(0, list);*/
            //setFrame = Mathf.FloorToInt(EditorGUILayout.IntSlider(setFrame, 0, script.maxFrames - 1));
            //EditorGUILayout.EndHorizontal();

            #endregion
            
            #region Line 3

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Previous Frame"))
            {
                script.PreviousFrame();
            }
            if (GUILayout.Button("Next Frame"))
            {
                script.NextFrame();
            }

            EditorGUILayout.EndHorizontal();

            #endregion

            #region Line 4

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("First Frame"))
            {
                script.FirstFrame();
            }
            if (GUILayout.Button("Last Frame"))
            {
                script.LastFrame();
            }

            EditorGUILayout.EndHorizontal();

            #endregion
            
        }
    }
}
#endif