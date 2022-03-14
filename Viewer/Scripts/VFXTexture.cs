using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace PointCloud.Player
{
    internal class VFXTexture
    {
        internal Texture tex_Cloud { get; }
        internal Texture tex_Color { get; }
        
        private int width;
        private int _maxPointCount;
        
        private int kernel;
        private ComputeShader VFXShader;
        private ComputeBuffer frameBuffer;
        
        private List<Vector3> _points;
        private List<Color32> _colors;

        internal VFXTexture(ComputeShader cShader, int maxPointCount = 300000)
        {
            VFXShader = cShader;
            kernel = VFXShader.FindKernel("TransferFrame");

            _maxPointCount = maxPointCount;
            VFXShader.SetInt("VertexCount", _maxPointCount);

            width = Mathf.CeilToInt(Mathf.Sqrt(_maxPointCount));
            VFXShader.SetInt("Width", width);

            tex_Cloud = new RenderTexture(width, width, 0, RenderTextureFormat.ARGBHalf)
                {enableRandomWrite = true, filterMode = FilterMode.Point};
            tex_Color = new RenderTexture(width, width, 0, RenderTextureFormat.ARGB32)
                {enableRandomWrite = true, filterMode = FilterMode.Point};
            
            frameBuffer = new ComputeBuffer(_maxPointCount * 3, sizeof(int), ComputeBufferType.Raw,
                ComputeBufferMode.SubUpdates);
        }

        /// <summary>
        /// Convert point cloud to Pos and Color textures from Spherum's archivation
        /// </summary>
        /// <param name="frame">Byte[] within int[] inside</param>
        internal void PointCloudToTexture(byte[] frame, float farFilter)
        {
            try
            {
                //-----Set frame array
                NativeArray<int> frameArray = frameBuffer.BeginWrite<int>(0, _maxPointCount * 3); 
                int[] frameAr = new int[_maxPointCount * 3];
                int realSize = frame.Length / sizeof(int);
                IntPtr framePtr = Marshal.AllocHGlobal(frame.Length);
                Marshal.Copy(frame, 0, framePtr, frame.Length);
                Marshal.Copy(framePtr, frameAr, 0, realSize);
                Marshal.FreeHGlobal(framePtr);

                frameArray.CopyFrom(frameAr);
                frameBuffer.EndWrite<int>(_maxPointCount * 3);
                //-----------

                //-----Set ComputeBuffer fields
                Debug.LogError(farFilter);
                VFXShader.SetFloat("maxDistance", farFilter);
                VFXShader.SetTexture(kernel, "PositionMap", tex_Cloud);
                VFXShader.SetTexture(kernel, "ColorMap", tex_Color);

                VFXShader.SetInt("PointLimit", realSize);

                VFXShader.SetBuffer(kernel, "FrameBuffer", frameBuffer);
                //-----------
                
                //------Process texture in ComputeBuffer
                VFXShader.Dispatch(kernel, width / 4, width / 4, 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
        internal async void PointCloudToTexture(int[] frame)
        {
            try
            {
                //-----Set frame array
                NativeArray<int> frameArray = frameBuffer.BeginWrite<int>(0, _maxPointCount * 3); 
                int[] frameAr = new int[_maxPointCount * 3];
                int realSize = frame.Length;
                IntPtr framePtr = Marshal.AllocHGlobal(frame.Length * sizeof(int));
                Marshal.Copy(frame, 0, framePtr, frame.Length);
                Marshal.Copy(framePtr, frameAr, 0, realSize);
                Marshal.FreeHGlobal(framePtr);

                frameArray.CopyFrom(frameAr);
                frameBuffer.EndWrite<int>(_maxPointCount * 3);
                //-----------

                //-----Set ComputeBuffer fields
                VFXShader.SetTexture(kernel, "PositionMap", tex_Cloud);
                VFXShader.SetTexture(kernel, "ColorMap", tex_Color);

                VFXShader.SetInt("VertexCount", _maxPointCount);
                VFXShader.SetInt("PointLimit", realSize);

                VFXShader.SetBuffer(kernel, "FrameBuffer", frameBuffer);
                //-----------
                
                //------Process texture in ComputeBuffer
                VFXShader.Dispatch(kernel, width / 4, width / 4, 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
    }
}
