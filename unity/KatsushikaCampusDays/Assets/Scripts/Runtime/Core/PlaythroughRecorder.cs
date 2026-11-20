using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KCD
{
    /// <summary>
    /// 通しプレイの録画役。-kcd-record &lt;出力先の拡張子なしのパス&gt; があるときだけ <see cref="PlaythroughDirector"/> が足す。
    ///
    /// 映像は <see cref="Time.captureFramerate"/> で 1 フレームを 1/30 秒に固定し、フレームごとに画面を ffmpeg の標準入力へ流す。
    /// 処理が実時間より遅くてもゲームの中の時間はフレーム数どおりに進むので、書き出した動画は等倍になる。
    /// 音は <see cref="AudioRenderer"/> でフレームごとに同じ長さぶんを書き出す。AudioRenderer が動いている間は
    /// スピーカーへ音が出ない。映像と音を合わせるのは録画のあと（ffmpeg で mux）。
    ///   出力: &lt;base&gt;.video.mp4（映像のみ）、&lt;base&gt;.audio.f32（32bit float、インターリーブ）、&lt;base&gt;.audio.txt（サンプルレートとチャンネル数）
    /// </summary>
    public sealed class PlaythroughRecorder : MonoBehaviour
    {
        /// <summary>録画のフレームレート。</summary>
        public const int FrameRate = 30;

        private string _basePath;
        private string _ffmpeg;
        private Process _process;
        private Stream _videoIn;
        private FileStream _audioOut;
        private NativeArray<float> _audioBuffer;
        private byte[] _audioBytes = new byte[0];
        private int _channels;
        private int _width;
        private int _height;
        private bool _recording;
        private long _frames;

        /// <summary>録画したフレーム数。</summary>
        public long Frames => _frames;

        /// <summary>録画中か。</summary>
        public bool IsRecording => _recording;

        /// <summary>Begin を一度呼んだか。失敗しても呼び直さないために使う。</summary>
        public bool Attempted => _attempted;

        private bool _attempted;
        private byte[] _videoBytes = new byte[0];

        public void Configure(string basePath, string ffmpegPath)
        {
            _basePath = basePath;
            _ffmpeg = string.IsNullOrEmpty(ffmpegPath) ? "ffmpeg" : ffmpegPath;
        }

        /// <summary>録画を始める。画面の大きさはこの時点のものに固定する（偶数に丸める）。</summary>
        public bool Begin()
        {
            if (_attempted || string.IsNullOrEmpty(_basePath))
            {
                return false;
            }

            _attempted = true;

            _width = Screen.width & ~1;
            _height = Screen.height & ~1;
            string folder = Path.GetDirectoryName(_basePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string video = _basePath + ".video.mp4";
            var info = new ProcessStartInfo
            {
                FileName = _ffmpeg,
                Arguments = string.Format(CultureInfo.InvariantCulture,
                    "-y -loglevel error -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -i - " +
                    "-vf vflip -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p \"{3}\"",
                    _width, _height, FrameRate, video),
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            try
            {
                _process = Process.Start(info);
                _videoIn = _process.StandardInput.BaseStream;
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] playthrough: ffmpeg を起動できない: " + error.Message);
                return false;
            }

            _channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            try
            {
                _audioOut = new FileStream(_basePath + ".audio.f32", FileMode.Create, FileAccess.Write);
                File.WriteAllText(_basePath + ".audio.txt", string.Format(CultureInfo.InvariantCulture,
                    "sample_rate={0}\nchannels={1}\nfps={2}\n", AudioSettings.outputSampleRate, _channels, FrameRate));
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] playthrough: 音のファイルを作れない: " + error.Message);
                CloseProcess();
                _audioOut?.Dispose();
                _audioOut = null;
                return false;
            }

            Time.captureFramerate = FrameRate;
            if (!AudioRenderer.Start())
            {
                Debug.LogWarning("[KCD] playthrough: AudioRenderer を始められない（音なしで続ける）");
            }

            AudioListener.volume = 1f;
            _recording = true;
            StartCoroutine(CaptureLoop());
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[KCD] playthrough: 録画開始 {0}x{1} @{2}fps 音 {3}Hz {4}ch",
                _width, _height, FrameRate, AudioSettings.outputSampleRate, _channels));
            return true;
        }

        private IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (_recording)
            {
                yield return endOfFrame;
                if (!_recording)
                {
                    break;
                }

                if (!WriteVideoFrame())
                {
                    End();
                    break;
                }

                WriteAudioFrame();
                _frames++;
            }
        }

        private bool WriteVideoFrame()
        {
            int size = _width * _height * 4;
            if (_videoBytes.Length != size)
            {
                _videoBytes = new byte[size];
            }

            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                if (shot.width != _width || shot.height != _height)
                {
                    // 窓の大きさが変わったときは 1 フレームぶん黒を流して長さだけ合わせる。
                    Array.Clear(_videoBytes, 0, size);
                    _videoIn.Write(_videoBytes, 0, size);
                    return true;
                }

                NativeArray<byte> raw = shot.GetRawTextureData<byte>();
                if (raw.Length == size)
                {
                    raw.CopyTo(_videoBytes);
                    _videoIn.Write(_videoBytes, 0, size);
                }
                else
                {
                    Color32[] pixels = shot.GetPixels32();
                    var bytes = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        bytes[i * 4] = pixels[i].r;
                        bytes[i * 4 + 1] = pixels[i].g;
                        bytes[i * 4 + 2] = pixels[i].b;
                        bytes[i * 4 + 3] = pixels[i].a;
                    }

                    _videoIn.Write(bytes, 0, bytes.Length);
                }

                return true;
            }
            catch (IOException error)
            {
                Debug.LogError("[KCD] playthrough: 映像を書けない: " + error.Message);
                return false;
            }
            finally
            {
                Destroy(shot);
            }
        }

        private void WriteAudioFrame()
        {
            int samples = AudioRenderer.GetSampleCountForCaptureFrame() * _channels;
            if (samples <= 0)
            {
                return;
            }

            if (!_audioBuffer.IsCreated || _audioBuffer.Length != samples)
            {
                if (_audioBuffer.IsCreated)
                {
                    _audioBuffer.Dispose();
                }

                _audioBuffer = new NativeArray<float>(samples, Allocator.Persistent);
            }

            AudioRenderer.Render(_audioBuffer);
            int bytes = samples * 4;
            if (_audioBytes.Length != bytes)
            {
                _audioBytes = new byte[bytes];
            }

            _audioBuffer.Reinterpret<byte>(4).CopyTo(_audioBytes);
            _audioOut.Write(_audioBytes, 0, bytes);
        }

        /// <summary>録画を止め、ffmpeg が書き終えるのを待つ。</summary>
        public void End()
        {
            if (_process == null)
            {
                return;
            }

            _recording = false;
            AudioListener.volume = 0f;
            AudioRenderer.Stop();
            Time.captureFramerate = 0;
            CloseProcess();
            _audioOut?.Dispose();
            _audioOut = null;
            if (_audioBuffer.IsCreated)
            {
                _audioBuffer.Dispose();
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[KCD] playthrough: 録画終了 frames={0} ({1:F1} 秒)", _frames, _frames / (double)FrameRate));
        }

        /// <summary>ffmpeg の入力を閉じて書き終えるのを待つ。2 分で終わらなければ止める。</summary>
        private void CloseProcess()
        {
            try
            {
                _videoIn?.Flush();
                _videoIn?.Close();
                if (!_process.WaitForExit(120000))
                {
                    Debug.LogError("[KCD] playthrough: ffmpeg が 2 分で終わらないので止める");
                    _process.Kill();
                }
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] playthrough: ffmpeg を閉じられない: " + error.Message);
            }

            _process.Dispose();
            _process = null;
            _videoIn = null;
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
