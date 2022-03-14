using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;
using Debug = UnityEngine.Debug;

namespace PointCloud.Player
{
    [ExecuteAlways]
    internal class ViewerPC : MonoBehaviour
    {
        [SerializeField] private VisualEffect vfx;

        [SerializeField] private ComputeShader computeShader;
        private VFXTexture _vfxTexture;

        /// <summary>
        /// Don't overwrite field! It's only for InspectorGUI
        /// </summary>
        [HideInInspector] public string Processed_Path;
        [SerializeField] private string fullPath;

        [SerializeField] private string[] files;
        private class PCFrame
        {
            internal long Index { get; }
            internal byte[] frame { get; }
            internal PCFrame(long frameIndex, byte[] frameAr)
            {
                Index = frameIndex;
                frame = frameAr;
            }
        }

        private Dictionary<long, PCFrame> _loadedDictionary;

        [SerializeField] private int maxPointCount = 300000;
        [SerializeField, Range(.0000001f, 2f)] private float pointSize = .001f;
        
        [SerializeField] private Vector2 bgRemoveDiaposone = new Vector2(0f, 20f);
        [SerializeField, Range(0f, 1f)] private float bgRemovePercent = 0f;
        private float _bgRemoveDistance;
        
        [SerializeField, Range(0, 5)] private int maxBackUpSize;
        private int _loadBias;
        private long _currentFrame;
        
        private Thread _loadThread;
        private Thread _cleanThread;
        private Coroutine _graphicRoutine;

        internal void Init(string path, int setPointCount = 0)
        {
            Dispose();
            _vfxTexture = new VFXTexture(computeShader, maxPointCount);
            _bgRemoveDistance = Mathf.Lerp(bgRemoveDiaposone.x, bgRemoveDiaposone.y, (1 - bgRemovePercent));
            maxPointCount = setPointCount < 1 ? maxPointCount : setPointCount;
            if (path != null && path != string.Empty)
            {
                Processed_Path = path;
                fullPath = Saver.CreatePath_CloudsBinary(Processed_Path);
                files = Saver.Binary_GetFrames(Processed_Path);
            }
            _loadedDictionary = new Dictionary<long, PCFrame>();
            vfx.Play();
        }
        public void SetEffect(VisualEffect effect)
        {
            Destroy(vfx);
        }

        internal void Dispose()
        {
            _vfxTexture = null;
            files = null;
            
            if (_loadedDictionary != null)
            {
                _loadedDictionary.Clear();
                _loadedDictionary = null;
            }
            if (_loadThread != null)
            {
                _loadThread.Abort();
                _loadThread = null;
            }

            if (_cleanThread != null)
            {
                _cleanThread.Abort();
                _cleanThread = null;
            }

            if (_graphicRoutine != null)
            {
                StopCoroutine(_graphicRoutine);
                _graphicRoutine = null;
            }
        }

        #region Playback methods

        internal void SetFrame(int[] frameData)
        {
            _vfxTexture.PointCloudToTexture(frameData);
            Texture texCloud = _vfxTexture.tex_Cloud;
            Texture texColor = _vfxTexture.tex_Color;
            
            if (vfx.HasFloat("Size")) vfx.SetFloat("Size", pointSize);
            if (vfx.HasUInt("PointCount")) vfx.SetUInt("PointCount", (uint)maxPointCount);
            if (vfx.HasTexture("Position Map")) vfx.SetTexture("Position Map", texCloud);
            if (vfx.HasTexture("Color Map")) vfx.SetTexture("Color Map", texColor);
            vfx.Reinit();
        }

        internal void SetFrame(long frameId)
        {
            _currentFrame = frameId;
            _loadBias = 0;

            if (_loadedDictionary == null)
            {
                _loadedDictionary = new Dictionary<long, PCFrame>();
            }
            if (_loadThread == null)
            {
                _loadThread = new Thread(LocalLoadFrame);
                _loadThread.Start();
            }

            if (_cleanThread == null)
            {
                _cleanThread = new Thread(ClearBackUp);
                _cleanThread.Start();
            }

            if (_graphicRoutine != null)
            {
                StopCoroutine(_graphicRoutine);
            }

            _graphicRoutine = StartCoroutine(SetFrame());
        }

        private IEnumerator SetFrame()
        {
            while (_loadedDictionary == null || _loadedDictionary!= null && !_loadedDictionary.ContainsKey(_currentFrame))
            {
                yield return null;
            }
            PCFrame frame;
            lock (_loadedDictionary)
            {
                _loadedDictionary.TryGetValue(_currentFrame, out frame);
                if (frame == null) yield break;
            }
            _vfxTexture.PointCloudToTexture(frame.frame, _bgRemoveDistance);
            Texture texCloud = _vfxTexture.tex_Cloud;
            Texture texColor = _vfxTexture.tex_Color;

            frame = null;
            
            if (vfx.HasFloat("Size")) vfx.SetFloat("Size", pointSize);
            if (vfx.HasUInt("PointCount")) vfx.SetUInt("PointCount", (uint)maxPointCount);
            if (vfx.HasTexture("Position Map")) vfx.SetTexture("Position Map", texCloud);
            if (vfx.HasTexture("Color Map")) vfx.SetTexture("Color Map", texColor);
            vfx.Reinit();
        }
        private async void LocalLoadFrame()
        {
            while (true)
            {
                while (_loadBias > maxBackUpSize)
                {
                    await Task.Delay(5);
                }
                long loadIndex = _currentFrame + _loadBias;
                
                if (loadIndex >= files.Length)
                {
                    loadIndex -= files.Length;
                }
                if (_loadedDictionary.ContainsKey(loadIndex))
                {
                    _loadBias++;
                    await Task.Delay(5);
                    continue;
                }
                string framePath = string.Format("{0}/{1}/{2}", fullPath, Saver.dirName_Frames, files[loadIndex]);

                Task[] tasks = new Task[1];
                byte[] frameAr = null;
                Func<object, byte[]> loadFunc = LocalLoadBin;

                tasks[0] = Task<byte[]>.Factory.StartNew(loadFunc, framePath).ContinueWith(res =>
                {
                    frameAr = res.Result;
                });
                
                try
                {
                    Task.WaitAll(tasks);
                }
                catch (AggregateException e)
                {
                    Debug.LogError($"Can't load data: {e.InnerExceptions}");
                    throw;
                }
                PCFrame newFrame = new PCFrame(loadIndex, frameAr);

                lock (_loadedDictionary)
                {
                    _loadedDictionary.Add(loadIndex, newFrame);
                }

                _loadBias++;
            }
        }
        private byte[] LocalLoadBin(object inData)
        {
            string path = (string) inData;
            byte[] returnData;
            using (BinaryReader br = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read), Encoding.Default, false))
            {
                returnData = br.ReadBytes((int) br.BaseStream.Length);
            }
            return returnData;
        }

        private async void ClearBackUp()
        {
            while (true)
            {
                await Task.Delay(5);
                if (_loadedDictionary == null || _loadedDictionary.Count <= maxBackUpSize) continue;

                foreach (var loaded in _loadedDictionary)
                {
                    if (loaded.Key < _currentFrame)
                    {
                        lock (_loadedDictionary)
                        {
                            _loadedDictionary.Remove(loaded.Key);
                        }
                        break;
                    }
                }
            }
        }

        #endregion

        internal void SetPointSize(float value)
        {
            pointSize = value;
        }

        internal void SetFarRemoveValue(float value)
        {
            value = Mathf.Clamp01(value);
            bgRemovePercent = value;
            _bgRemoveDistance = Mathf.Lerp(bgRemoveDiaposone.x, bgRemoveDiaposone.y, (1 - bgRemovePercent));
            SetFrame(_currentFrame);
        }
    }
}
