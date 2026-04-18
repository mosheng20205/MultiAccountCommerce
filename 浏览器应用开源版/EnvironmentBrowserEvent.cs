using System;
using FBroSharp.Const;
using FBroSharp.DataType;
using FBroSharp.Event;
using FBroSharp.Lib;
using FBroSharp.Value;
using FBroSharp.Callback;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed class EnvironmentBrowserEvent : FBroSharpBrowserEvent
    {
        private readonly Action<string, string> _addressChanged;
        private readonly Action<string, string> _titleChanged;
        private readonly Action<string> _browserClosed;

        public EnvironmentBrowserEvent(
            Action<string, string> addressChanged,
            Action<string, string> titleChanged,
            Action<string> browserClosed)
        {
            _addressChanged = addressChanged;
            _titleChanged = titleChanged;
            _browserClosed = browserClosed;
        }

        public override void OnAfterCreated(IFBroSharpBrowser browser, IFBroSharpDictionaryValue extrainfo)
        {
            IFBroSharpFrame frame = browser.GetMainFrame();
            if (frame != null)
            {
                _addressChanged?.Invoke(browser.GetUserFlag(), frame.GetURL());
            }
        }

        public override void OnAddressChange(IFBroSharpBrowser browser, IFBroSharpFrame frame, string url)
        {
            _addressChanged?.Invoke(browser.GetUserFlag(), url);
        }

        public override void OnTitleChange(IFBroSharpBrowser browser, string title)
        {
            _titleChanged?.Invoke(browser.GetUserFlag(), title);
        }

        public override void OnBeforeClose(IFBroSharpBrowser browser)
        {
            _browserClosed?.Invoke(browser.GetUserFlag());
        }

        public override bool DoClose(IFBroSharpBrowser browser)
        {
            return false;
        }

        public override bool OnBeforePopup(
            IFBroSharpBrowser browser,
            IFBroSharpFrame frame,
            int popup_id,
            string target_url,
            string target_frame_name,
            FBroSharpWindowOpenDisposition target_disposition,
            bool user_gesture,
            FBroSharpPopupfeatures popupFeatures,
            ref FBroSharpWindowsInfo windowInfo,
            ref FBroSharpBrowserSetting settings,
            ref bool no_javascript_access,
            IFBroSharpUseExtraData user_settings)
        {
            IFBroSharpFrame mainFrame = browser.GetMainFrame();
            if (mainFrame != null)
            {
                mainFrame.LoadURL(target_url);
            }

            return true;
        }

        public override bool OnCertificateError(IFBroSharpBrowser browser, int cert_error, string request_url, IFBroSharpSSLInfo ssl_info, IFBroSharpCallback callback)
        {
            callback.Continue();
            return true;
        }
    }
}
