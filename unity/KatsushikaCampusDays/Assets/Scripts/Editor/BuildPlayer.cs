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
            PlayerSettings.WebGL.template = "APPLICATION:Default";
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
