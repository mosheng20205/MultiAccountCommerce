using System;
using System.IO;
using FBroSharp;
using FBroSharp.Const;
using FBroSharp.DataType;
using FBroSharp.Event;
using FBroSharp.Lib;
using FBroSharp.VIP;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal static class FBroRuntime
    {
        private const string AuthorizationCode = "7219F1055511223A5E17B4E9717D5C4AAED35B1F41746E7668A05100C6593FB2";
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string globalCachePath = Path.Combine(baseDirectory, "fbro-global-cache");
            string subprocessPath = Path.Combine(baseDirectory, "FBroSubprocess.exe");

            Directory.CreateDirectory(globalCachePath);
            FBroSharpVIPGlobalControl.SetAuthorizationCode(AuthorizationCode);

            if (!File.Exists(subprocessPath))
            {
                throw new FileNotFoundException("FBroSubprocess.exe was not found in the application output directory.", subprocessPath);
            }

            FBroSharpInitSet set = new FBroSharpInitSet
            {
                cache_path = globalCachePath,
                multi_threaded_message_loop = true,
                browser_subprocess_path = subprocessPath,
                locale = "zh-CN",
                persist_session_cookies = true,
                log_severity = FBroSharpLogSeverity.ERROR,
            };

            if (!FBroSharpInitControl.InitPro(set, new FBroBrowserInitEvent()))
            {
                throw new InvalidOperationException("FBroSharpInitControl.InitPro failed.");
            }

            _initialized = true;
        }

        public static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            FBroSharpInitControl.Shutdown(false);
            _initialized = false;
        }
    }

    internal sealed class FBroBrowserInitEvent : FBroInitEvent
    {
        public override void OnBeforeCommandLineProcessing(string processType, IFBroSharpCommandLine commandLine)
        {
            commandLine.DisableGpuBlockList();
            commandLine.DisableGpuCache();
            commandLine.AppendSwitch("-disable-chrome-login-prompt"); // ∆¡±ŒµØ≥ˆ’À∫≈√‹¬Î…Ë÷√Ã· æ
            commandLine.AppendSwitchWithValue("--disable-features", "Translate,TranslateUI"); //∆¡±Œ∑≠“ÎµØ¥∞
        }
    }
}
