using System.Text.RegularExpressions;
using UnityEditor;

namespace KCD.Editor
{
    /// <summary>
    /// キャラの顔テクスチャ（Assets/Models/Characters/&lt;id&gt;/face.png）の WebGL の取り込み規則 (#70)。
    /// WebGL では最大サイズを <see cref="WebGLMaxSize"/> に下げる。Windows 版は 2048 のまま。
    /// <para>
    /// 顔は 2048² の右上の 1/4 にある。テクスチャの大半は空白で gzip がよく効くが, 2048 のままだと
    /// 8 枚で WebGL の data（gzip 後）の約 0.7 MiB を占める。1024 にすると約 0.55 MiB 減り, 顔の部分は 512² になる。
    /// いちばん近いカメラ（三人称の最短 1.4 m, 画角 55°, 1080p）で頭の縦は約 190 px なので, 顔の部分の解像度はそれより高い。
    /// </para>
    /// <para>
    /// 形式は自動のまま上書きするので, DXT と ASTC のどちらのビルドでも, そのビルドの形式で圧縮される（BuildPlayer.BuildWebGL）。
    /// </para>
    /// </summary>
    public sealed class CharacterFaceImportRules : AssetPostprocessor
    {
        public const int WebGLMaxSize = 1024;

        private static readonly Regex FacePath = new Regex(@"^Assets/Models/Characters/[^/]+/face\.png$");

        public override uint GetVersion()
        {
            return 1;
        }

        private void OnPreprocessTexture()
        {
            if (!FacePath.IsMatch(assetPath))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            TextureImporterPlatformSettings web = importer.GetPlatformTextureSettings("WebGL");
            if (web.overridden && web.maxTextureSize == WebGLMaxSize)
            {
                return;
            }

            web.overridden = true;
            web.maxTextureSize = WebGLMaxSize;
            web.format = TextureImporterFormat.Automatic;
            web.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(web);
        }
    }
}
