using NUnit.Framework;
using UnityEngine;

/// <summary>
/// PlayMode のテストの間はゲームの音（BGM・効果音・声）を出さない。鳴らす処理はそのまま動かし、
/// 聞こえる音量だけを 0 にする。名前空間の外に置くと、このアセンブリの全テストの前後で 1 回ずつ走る。
/// </summary>
[SetUpFixture]
public sealed class MuteDuringTests
{
    private float _volume = 1f;

    [OneTimeSetUp]
    public void Mute()
    {
        _volume = AudioListener.volume;
        AudioListener.volume = 0f;
    }

    [OneTimeTearDown]
    public void Restore()
    {
        AudioListener.volume = _volume;
    }
}
