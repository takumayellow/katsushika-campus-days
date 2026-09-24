using System;
using System.Globalization;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// Web 版の写真の受け渡し (#61)。ブラウザにダウンロードさせるのは Web 版のプレイヤーだけで、
    /// エディタとデスクトップは今までどおり persistentDataPath/Photos に置く。写真のファイル名は端末の暦に依らない。
    /// </summary>
    public sealed class WebDownloadTests
    {
        private CultureInfo _culture;

        [SetUp]
        public void SetUp()
        {
            _culture = CultureInfo.CurrentCulture;
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _culture;
        }

        [Test]
        public void CanSend_NeedsANameAndSomeBytes()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

            Assert.IsTrue(WebDownload.CanSend("kcd_20260924_070503.png", png));
            Assert.IsFalse(WebDownload.CanSend(null, png));
            Assert.IsFalse(WebDownload.CanSend(string.Empty, png));
            Assert.IsFalse(WebDownload.CanSend("kcd.png", null));
            Assert.IsFalse(WebDownload.CanSend("kcd.png", new byte[0]));
        }

        [Test]
        public void OutsideTheWebPlayer_NothingIsHandedToTheBrowser()
        {
            // エディタではダウンロードの口を持たないので、PhotoSystem はファイルに書く道を通る。
            Assert.IsFalse(WebDownload.Supported);
            Assert.IsFalse(WebDownload.Send("kcd.png", new byte[] { 1 }, "image/png"));
        }

        [Test]
        public void FileName_IsTheShootingTime()
        {
            Assert.AreEqual("kcd_20260924_070503.png", PhotoSystem.FileNameFor(new DateTime(2026, 9, 24, 7, 5, 3)));
        }

        [Test]
        public void FileName_UsesTheWesternYear_WhateverTheDeviceLanguage()
        {
            // タイ語の既定の暦は仏暦で、その暦のまま書くと年が 2569 になる。
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            Assert.AreEqual("kcd_20260924_190503.png", PhotoSystem.FileNameFor(new DateTime(2026, 9, 24, 19, 5, 3)));
        }
    }
}
