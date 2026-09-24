using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Windows / WebGL 向けのプレイヤーを出力する。バッチから -executeMethod で呼ぶ。
    /// 出力先は -buildOutput &lt;path&gt; で差し替えられる。
    /// </summary>
    public static class BuildPlayer
    {
        private const string DefaultOutput = "../../build/Windows/KatsushikaCampusDays.exe";

        /// <summary>WebGL の既定出力先。index.html と Build/ がこの下にできる。</summary>
        private const string DefaultWebOutput = "../../build/WebGL";

        /// <summary>EditorBuildSettings に入っているシーンをまとめて 1 本の exe にする。</summary>
        [MenuItem("KCD/Windows ビルド")]
        public static void BuildWindows()
        {
            string output = ResolveOutput();
            string folder = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                EditorPaths.Report("ビルド対象のシーンがありません。先に SceneBuilder.BuildAll を実行してください。");
                EditorApplication.Exit(1);
                return;
            }

            if (!EnsureActiveTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                EditorApplication.Exit(1);
                return;
            }

            Prepare();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            EditorPaths.Report("ビルド結果: " + summary.result
                + " / " + summary.totalSize + " bytes / " + output);
            EditorPaths.AppendVerify("build " + summary.result + " " + output);

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// GitHub Pages に置く WebGL ビルド。Pages は .gz に Content-Encoding を付けないので、
        /// Gzip + 復元フォールバック（ローダーが JS で展開する）にする。
        /// </summary>
        [MenuItem("KCD/WebGL ビルド")]
        public static void BuildWebGL()
        {
            string output = ResolveWebOutput();
            Directory.CreateDirectory(output);

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                EditorPaths.Report("ビルド対象のシーンがありません。先に SceneBuilder.BuildAll を実行してください。");
                EditorApplication.Exit(1);
                return;
            }

            if (!EnsureActiveTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                EditorApplication.Exit(1);
                return;
            }

            Prepare();
            PrepareWebGL();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            EditorPaths.Report("WebGL ビルド結果: " + summary.result
                + " / " + summary.totalSize + " bytes / " + output);
            EditorPaths.AppendVerify("build-webgl " + summary.result + " " + output);

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 作業中のプラットフォームをビルド先に合わせる。
        /// URP はビルド前処理で EditorUserBuildSettings.activeBuildTarget の品質レベルから URP アセットを集め,
        /// その設定で Shader のバリアントを絞る。Windows のまま WebGL を出すと PC_RPAsset (Forward+) で絞られ,
        /// WebGL が実際に使う Mobile_RPAsset (Forward) 用の ForwardLit が 1 つも入らず, 建物と地面が描かれない。
        /// 起動時に -buildTarget を付ければ切り替えは起きない。
        /// </summary>
        private static bool EnsureActiveTarget(BuildTargetGroup group, BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                return true;
            }

            EditorPaths.Report("作業中のプラットフォームを " + EditorUserBuildSettings.activeBuildTarget
                + " から " + target + " に切り替える (URP の Shader 絞り込みをビルド先に合わせる)");
            if (EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                return true;
            }

            EditorPaths.Report("プラットフォームを " + target + " に切り替えられなかった。モジュールが入っているか確認する。");
            return false;
        }

        /// <summary>-buildOutput があればそれを、無ければリポジトリ直下の build/WebGL を使う。</summary>
        private static string ResolveWebOutput()
        {
            string argument = EditorPaths.ReadArgument("-buildOutput", string.Empty);
            if (string.IsNullOrEmpty(argument))
            {
                return EditorPaths.ProjectRelative(DefaultWebOutput);
            }

            return Path.IsPathRooted(argument) ? argument : EditorPaths.ProjectRelative(argument);
        }

        /// <summary>
        /// 静的ホスティング向けの WebGL 設定。
        /// Gzip + decompressionFallback は、サーバーが Content-Encoding を返さなくても動く組み合わせ。
        /// スレッドは SharedArrayBuffer の COOP/COEP ヘッダーが要るので使わない。
        /// </summary>
        private static void PrepareWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            // 自前のテンプレート（Assets/WebGLTemplates/KCD）。大きな全画面ボタンと、
            // 「Esc でマウスを解放 / F で全画面」の案内をページに常に出す (#48)。
            PlayerSettings.WebGL.template = "PROJECT:KCD";
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
            PlayerSettings.runInBackground = true;
        }

        /// <summary>-buildOutput があればそれを、無ければリポジトリ直下の build/Windows を使う。</summary>
        private static string ResolveOutput()
        {
            string argument = EditorPaths.ReadArgument("-buildOutput", string.Empty);
            if (string.IsNullOrEmpty(argument))
            {
                return EditorPaths.ProjectRelative(DefaultOutput);
            }

            if (!argument.EndsWith(".exe"))
            {
                argument = Path.Combine(argument, "KatsushikaCampusDays.exe");
            }

            return Path.IsPathRooted(argument) ? argument : EditorPaths.ProjectRelative(argument);
        }

        private static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        /// <summary>製品名まわりを揃える。exe 名とウィンドウ名が食い違わないようにする。</summary>
        private static void Prepare()
        {
            PlayerSettings.companyName = "KCD";
            PlayerSettings.productName = "Katsushika Campus Days";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }
    }
}
