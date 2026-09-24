// Web 版で、ゲームが作ったファイルをブラウザにダウンロードさせる (#61)。C# 側は Scripts/Runtime/Core/WebDownload.cs。
// Web 版の persistentDataPath は IndexedDB の中にあり、プレイヤーが取り出す手段が無いので、写真はここから渡す。
mergeInto(LibraryManager.library, {
  KCD_DownloadFile__deps: ['$UTF8ToString'],
  KCD_DownloadFile: function (fileNamePtr, dataPtr, length, mimeTypePtr) {
    try {
      var fileName = UTF8ToString(fileNamePtr);
      var mimeType = UTF8ToString(mimeTypePtr) || 'application/octet-stream';

      // HEAPU8 はメモリが伸びると差し替わるので、呼ばれたときに読む。
      // slice で写してから Blob にする（ヒープをそのまま渡すと、あとの書き込みで中身が変わる）。
      var bytes = HEAPU8.slice(dataPtr, dataPtr + length);
      var url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));

      var link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.style.display = 'none';
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      // すぐに revoke すると、ブラウザによってはダウンロードが始まる前に中身が消える。少し待ってから捨てる。
      setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
      return 1;
    } catch (error) {
      console.warn('[KCD] ファイルをダウンロードさせられません: ' + error);
      return 0;
    }
  }
});
