using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FFmpegOut
{
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("FFmpegOut/Camera Capture")]
    public class CameraCapture : MonoBehaviour
    {
        #region Editable properties

        [SerializeField] bool _setResolution = true;
        [SerializeField] int _width = 1280;
        [SerializeField] int _height = 720;
        [SerializeField] int _frameRate = 30;
        [SerializeField] bool _allowSlowDown = true;
        [SerializeField] FFmpegPipe.Preset _preset;
        [SerializeField] float _startTime = 0;
        [SerializeField] float _recordLength = 5;

        #endregion

        #region Private members

        [SerializeField, HideInInspector] Shader _shader;
        Material _material;

        Queue<AsyncGPUReadbackRequest> _readbackQueue;
        byte[] _rawDataBuffer;

        FFmpegPipe _pipe;
        float _elapsed;

        RenderTexture _tempTarget;
        GameObject _tempBlitter;

        static int _activePipeCount;

        #endregion

        #region MonoBehavior functions

        void OnValidate()
        {
            _startTime = Mathf.Max(_startTime, 0);
            _recordLength = Mathf.Max(_recordLength, 0.01f);
        }

        void OnEnable()
        {
            if (!FFmpegConfig.CheckAvailable)
            {
                Debug.LogError(
                    "ffmpeg.exe is missing. " +
                    "Please refer to the installation instruction. " +
                    "https://github.com/keijiro/FFmpegOut"
                );
                enabled = false;
            }
        }

        void OnDisable()
        {
            if (_pipe != null) ClosePipe();
        }

        void OnDestroy()
        {
            if (_pipe != null) ClosePipe();
        }

        void Start()
        {
            _readbackQueue = new Queue<AsyncGPUReadbackRequest>();
            _material = new Material(_shader);
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            if (_startTime <= _elapsed && _elapsed < _startTime + _recordLength)
            {
                if (_pipe == null) OpenPipe();

                while (_readbackQueue.Count > 0)
                {
                    var req = _readbackQueue.Peek();

                    if (req.hasError)
                    {
                        Debug.Log("GPU readback error detected.");
                        _readbackQueue.Dequeue();
                    }
                    else if (req.done)
                    {
                        var data = req.GetData<byte>();
                        if (_rawDataBuffer == null)
                            _rawDataBuffer = new byte[data.Length];
                        //data.CopyTo(_rawDataBuffer);
                        Marshal.Copy(data.GetUnsafePtr(), _rawDataBuffer, 0, data.Length);
                        _pipe.Write(_rawDataBuffer);
                        _readbackQueue.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }

            }
            else
            {
                if (_pipe != null) ClosePipe();
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_pipe != null)
            {
                if (_readbackQueue.Count < 8)
                    _readbackQueue.Enqueue(AsyncGPUReadback.Request(source));
                else
                    Debug.Log("Too many requests.");
            }

            Graphics.Blit(source, destination);
        }

        #endregion

        #region Private methods

        void OpenPipe()
        {
            if (_pipe != null) return;

            var camera = GetComponent<Camera>();
            var width = _width;
            var height = _height;

            // Apply the screen resolution settings.
            if (_setResolution)
            {
                _tempTarget = RenderTexture.GetTemporary(width, height, 24);
                camera.targetTexture = _tempTarget;
                _tempBlitter = Blitter.CreateGameObject(camera);
            }
            else
            {
                width = camera.pixelWidth;
                height = camera.pixelHeight;
            }

            // Open an output stream.
            _pipe = new FFmpegPipe(name, width, height, _frameRate, _preset);
            _activePipeCount++;

            // Change the application frame rate on the first pipe.
            if (_activePipeCount == 1)
            {
                if (_allowSlowDown)
                    Time.captureFramerate = _frameRate;
                else
                    Application.targetFrameRate = _frameRate;
            }

            Debug.Log("Capture started (" + _pipe.Filename + ")");
        }

        void ClosePipe()
        {
            var camera = GetComponent<Camera>();

            // Destroy the blitter object.
            if (_tempBlitter != null)
            {
                Destroy(_tempBlitter);
                _tempBlitter = null;
            }

            // Release the temporary render target.
            if (_tempTarget != null && _tempTarget == camera.targetTexture)
            {
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(_tempTarget);
                _tempTarget = null;
            }

            // Close the output stream.
            if (_pipe != null)
            {
                Debug.Log("Capture ended (" + _pipe.Filename + ")");

                _pipe.Close();
                _activePipeCount--;

                if (!string.IsNullOrEmpty(_pipe.Error))
                {
                    Debug.LogWarning(
                        "ffmpeg returned with a warning or an error message. " +
                        "See the following lines for details:\n" + _pipe.Error
                    );
                }

                _pipe = null;

                // Reset the application frame rate on the last pipe.
                if (_activePipeCount == 0)
                {
                    if (_allowSlowDown)
                        Time.captureFramerate = 0;
                    else
                        Application.targetFrameRate = -1;
                }
            }
        }

        #endregion
    }
}
