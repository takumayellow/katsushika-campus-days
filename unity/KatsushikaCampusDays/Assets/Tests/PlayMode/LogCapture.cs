using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>PlayMode テストで拾ったログ 1 行。</summary>
    public readonly struct CapturedLog
    {
        public CapturedLog(LogType type, string message, string stack, int frame, bool afterSceneLoaded)
        {
            Type = type;
            Message = message ?? string.Empty;
            Stack = stack ?? string.Empty;
            Frame = frame;
            AfterSceneLoaded = afterSceneLoaded;
        }

        public LogType Type { get; }
        public string Message { get; }
        public string Stack { get; }
        public int Frame { get; }

        /// <summary>
        /// 待っているシーンの sceneLoaded より後に出たか。前なら読み込み中（Awake / OnEnable）、
        /// 後なら Start 以降に出た行。
        /// </summary>
        public bool AfterSceneLoaded { get; }

        public bool IsError => Type == LogType.Error || Type == LogType.Exception || Type == LogType.Assert;

        public override string ToString()
        {
            string firstLine = Message.Split('\n')[0].TrimEnd('\r');
            return "[" + Type + ", frame " + Frame + ", " + (AfterSceneLoaded ? "読み込み後" : "読み込み中") + "] " + firstLine;
        }
    }

    /// <summary>
    /// シーンを読む前から Debug.Log を拾っておき、テストの最後にエラーと例外を数える (#67)。
    /// LogAssert に任せると最初の 1 行で落ちてどの行かしか分からないので、全部集めてから行ごとに出す。
    /// </summary>
    public sealed class LogCapture : IDisposable
    {
        /// <summary>
        /// NavMesh に載れなかった NPC の行（#63）。tools/check_unity_log.py の NAVMESH と同じ式。
        /// ふつうのスモークでは数えず、NPC の NavMesh のテストだけが見る。
        /// </summary>
        public static readonly Regex NavMeshFailure =
            new Regex("Failed to create agent|can only be called on an active agent", RegexOptions.CultureInvariant);

        /// <summary>失敗メッセージに並べる行の上限。</summary>
        public const int MaxListed = 12;

        private readonly List<CapturedLog> _entries = new List<CapturedLog>();
        private string _watchedScene;
        private bool _disposed;

        public LogCapture()
        {
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>待っているシーンの sceneLoaded が来たか。</summary>
        public bool SceneLoaded { get; private set; }

        public IReadOnlyList<CapturedLog> Entries => _entries;

        /// <summary>これから読むシーン。sceneLoaded を待ち直し、以後の行の「読み込み中 / 後」をこれで分ける。</summary>
        public void Watch(string sceneName)
        {
            _watchedScene = sceneName;
            SceneLoaded = false;
        }

        /// <summary>エラー・例外・Assert の行。includeNavMesh が false なら NavMesh の失敗の行は除く。</summary>
        public List<CapturedLog> Errors(bool includeNavMesh)
        {
            var found = new List<CapturedLog>();
            foreach (CapturedLog entry in _entries)
            {
                if (entry.IsError && (includeNavMesh || !NavMeshFailure.IsMatch(entry.Message)))
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        /// <summary>種類を問わず pattern に合う行。</summary>
        public List<CapturedLog> Matching(Regex pattern)
        {
            var found = new List<CapturedLog>();
            foreach (CapturedLog entry in _entries)
            {
                if (pattern.IsMatch(entry.Message))
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        /// <summary>行を最大 MaxListed 行まで並べる。例外は呼び出し元が分かるようスタックの先頭も付ける。</summary>
        public static string Describe(IList<CapturedLog> logs)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < logs.Count && i < MaxListed; i++)
            {
                builder.Append("\n  ").Append(logs[i]);
                if (logs[i].Type == LogType.Exception && logs[i].Stack.Length > 0)
                {
                    builder.Append("\n    at ").Append(logs[i].Stack.Split('\n')[0].TrimEnd('\r'));
                }
            }

            if (logs.Count > MaxListed)
            {
                builder.Append("\n  ... ほか ").Append(logs.Count - MaxListed).Append(" 行");
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Application.logMessageReceived -= OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            _entries.Add(new CapturedLog(type, message, stack, Time.frameCount, SceneLoaded));
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == _watchedScene)
            {
                SceneLoaded = true;
            }
        }
    }
}
