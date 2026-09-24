using System;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// テストの間だけ PlayerPrefs の文字列のキーを 1 つ預かり、Dispose で入る前の状態に戻す (#101)。
    /// 入る前にキーが無かったら消し、有ったら元の文字列を書き戻して Save する。
    ///
    /// Editor の PlayerPrefs はプロジェクトごとに 1 つの置き場で、開発機のゲーム設定そのもの。
    /// PlayerPrefs に書く処理（<c>L.SetLocale</c>、<c>GameManager.SelectedCharacterId</c> の setter）を
    /// 通すテストはこれで囲む。値だけ覚えて書き戻すと、キーの無かった機械にキーを作って残す。
    /// メモリ上の状態（<c>L.Locale</c> など）は戻さないので、それはテストの側で先に戻す。
    /// </summary>
    public sealed class PlayerPrefsKeyScope : IDisposable
    {
        private readonly string _key;
        private readonly bool _hadKey;
        private readonly string _saved;
        private bool _restored;

        public PlayerPrefsKeyScope(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("PlayerPrefs のキーが空", nameof(key));
            }

            _key = key;
            _hadKey = PlayerPrefs.HasKey(key);
            _saved = _hadKey ? PlayerPrefs.GetString(key) : null;
        }

        /// <summary>入る前の状態に戻す。2 回目からは何もしない（あとから書いた値を巻き戻さない）。</summary>
        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            if (_hadKey)
            {
                PlayerPrefs.SetString(_key, _saved);
            }
            else
            {
                PlayerPrefs.DeleteKey(_key);
            }

            PlayerPrefs.Save();
        }
    }
}
