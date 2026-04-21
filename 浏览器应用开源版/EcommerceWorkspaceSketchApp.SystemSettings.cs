using System;
using System.Diagnostics;
using System.IO;
using EmojiWindowDemo;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed partial class EcommerceWorkspaceSketchApp
    {
        private const int PictureScaleFit = 2;
        private const string QqGroupUrl = "https://qm.qq.com/q/RSep1b1MwU";
        private const string QrImageFileName = "qrcode_523446917.jpg";
        private const string SystemSettingsNoteText = "此浏览器框架基于FBrowserCEF3lib";
        private const int SystemSettingsPictureBoxWidth = 420;
        private const int SystemSettingsPictureBoxHeight = 600;

        private void InitializeSystemSettingsUi()
        {
            _systemSettingsPanel = EmojiWindowNative.CreatePanel(_browserCanvas, 0, 0, 100, 100, Argb(255, 249, 251, 253));
            _systemSettingsImage = EmojiWindowNative.CreatePictureBox(_systemSettingsPanel, 0, 0, 100, 100, PictureScaleFit, CurrentSystemSettingsImageBackgroundColor());
            _lblSystemSettingsHint = Label(_systemSettingsPanel, "扫码加入 QQ 群", 14, true);
            _lblSystemSettingsNote = Label(_systemSettingsPanel, SystemSettingsNoteText, 11, false);
            _btnJoinQqGroup = Button(_systemSettingsPanel, "点击进入QQ群", Argb(255, 37, 99, 235), OpenQqGroupLink);

            SetLabelAlignmentSafe(_lblSystemSettingsHint, 1);
            SetLabelAlignmentSafe(_lblSystemSettingsNote, 1);
            EmojiWindowNative.SetPictureBoxBackgroundColor(_systemSettingsImage, CurrentSystemSettingsImageBackgroundColor());
            ApplySystemSettingsImageScaleMode();
            LoadSystemSettingsQrImage();
            SetSystemSettingsVisible(false);
        }

        private void RenderSystemSettings()
        {
            bool imageLoaded = LoadSystemSettingsQrImage();
            ApplySystemSettingsImageScaleMode();
            SetLabelText(_lblSystemSettingsHint, imageLoaded ? "扫码加入 QQ 群" : "二维码图片未找到，请点击下方按钮加入 QQ 群");
            SetLabelText(_lblSystemSettingsNote, SystemSettingsNoteText);
            SetLabelText(_lblInfoMain, "当前模块：系统设置");
            SetLabelText(_lblInfoSub, "扫码可直接加入 QQ 群，也可点击按钮通过系统默认浏览器打开群链接。");
            SetWindowTitle("电商多账号浏览器 - 系统设置");
        }

        private void SetSystemSettingsVisible(bool visible)
        {
            int show = visible ? SwShow : SwHide;
            if (_systemSettingsPanel != IntPtr.Zero)
            {
                NativeExtras.ShowWindow(_systemSettingsPanel, show);
            }

            if (_systemSettingsImage != IntPtr.Zero)
            {
                EmojiWindowNative.ShowPictureBox(_systemSettingsImage, visible ? 1 : 0);
            }

            foreach (IntPtr label in new[] { _lblSystemSettingsHint, _lblSystemSettingsNote })
            {
                if (label != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowLabel(label, visible ? 1 : 0);
                }
            }

            if (_btnJoinQqGroup != 0)
            {
                EmojiWindowNative.ShowButton(_btnJoinQqGroup, visible ? 1 : 0);
            }
        }

        private void LayoutSystemSettingsPage()
        {
            if (_systemSettingsPanel == IntPtr.Zero || _browserCanvasWidth <= 0 || _browserCanvasHeight <= 0)
            {
                return;
            }

            Move(_systemSettingsPanel, 0, 0, _browserCanvasWidth, _browserCanvasHeight);

            int outer = Scale(24);
            int hintHeight = Scale(28);
            int noteHeight = Scale(22);
            int buttonHeight = Scale(40);
            int buttonWidth = Math.Min(Scale(220), Math.Max(Scale(180), _browserCanvasWidth - outer * 2));
            int gapHintToImage = Scale(12);
            int gapImageToButton = Scale(18);
            int gapButtonToNote = Scale(14);

            int maxImageWidth = Math.Max(Scale(220), _browserCanvasWidth - outer * 2);
            int maxImageHeight = Math.Max(Scale(320), _browserCanvasHeight - hintHeight - noteHeight - buttonHeight - gapHintToImage - gapImageToButton - gapButtonToNote - outer * 2);

            int imageWidth = Scale(SystemSettingsPictureBoxWidth);
            int imageHeight = Scale(SystemSettingsPictureBoxHeight);
            float boxAspectRatio = SystemSettingsPictureBoxWidth / (float)SystemSettingsPictureBoxHeight;

            if (imageWidth > maxImageWidth)
            {
                imageWidth = maxImageWidth;
                imageHeight = (int)Math.Round(imageWidth / boxAspectRatio);
            }

            if (imageHeight > maxImageHeight)
            {
                imageHeight = maxImageHeight;
                imageWidth = (int)Math.Round(imageHeight * boxAspectRatio);
            }

            imageWidth = Math.Max(Scale(220), Math.Min(maxImageWidth, imageWidth));
            imageHeight = Math.Max(Scale(320), Math.Min(maxImageHeight, imageHeight));

            int totalHeight = hintHeight + gapHintToImage + imageHeight + gapImageToButton + buttonHeight + gapButtonToNote + noteHeight;
            int startY = Math.Max(outer, (_browserCanvasHeight - totalHeight) / 2);
            int hintY = startY;
            int imageY = hintY + hintHeight + gapHintToImage;
            int buttonY = imageY + imageHeight + gapImageToButton;
            int noteY = buttonY + buttonHeight + gapButtonToNote;
            int imageX = (_browserCanvasWidth - imageWidth) / 2;
            int buttonX = (_browserCanvasWidth - buttonWidth) / 2;

            EmojiWindowNative.SetLabelBounds(_lblSystemSettingsHint, outer, hintY, _browserCanvasWidth - outer * 2, hintHeight);
            EmojiWindowNative.SetPictureBoxBounds(_systemSettingsImage, imageX, imageY, imageWidth, imageHeight);
            EmojiWindowNative.SetPictureBoxBackgroundColor(_systemSettingsImage, CurrentSystemSettingsImageBackgroundColor());
            ApplySystemSettingsImageScaleMode();
            EmojiWindowNative.SetLabelBounds(_lblSystemSettingsNote, outer, noteY, _browserCanvasWidth - outer * 2, noteHeight);

            EmojiWindowNative.SetButtonBounds(_btnJoinQqGroup, buttonX, buttonY, buttonWidth, buttonHeight);
        }

        private bool LoadSystemSettingsQrImage()
        {
            if (_systemSettingsImage == IntPtr.Zero)
            {
                return false;
            }

            string imagePath = ResolveSystemSettingsQrPath();
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                EmojiWindowNative.ClearImage(_systemSettingsImage);
                return false;
            }

            byte[] pathBytes = U(imagePath);
            EmojiWindowNative.SetPictureBoxScaleMode(_systemSettingsImage, PictureScaleFit);
            EmojiWindowNative.SetImageOpacity(_systemSettingsImage, 1.0f);
            bool loaded = EmojiWindowNative.LoadImageFromFile(_systemSettingsImage, pathBytes, pathBytes.Length) != 0;
            ApplySystemSettingsImageScaleMode();
            return loaded;
        }

        private void ApplySystemSettingsImageScaleMode()
        {
            if (_systemSettingsImage == IntPtr.Zero)
            {
                return;
            }

            EmojiWindowNative.SetPictureBoxBackgroundColor(_systemSettingsImage, CurrentSystemSettingsImageBackgroundColor());
            EmojiWindowNative.SetPictureBoxScaleMode(_systemSettingsImage, PictureScaleFit);
            EmojiWindowNative.SetImageOpacity(_systemSettingsImage, 1.0f);
        }

        private uint CurrentSystemSettingsImageBackgroundColor()
        {
            return EmojiWindowNative.IsDarkMode() != 0
                ? Argb(255, 12, 15, 20)
                : Argb(255, 255, 255, 255);
        }

        private string ResolveSystemSettingsQrPath()
        {
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, QrImageFileName),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", QrImageFileName)),
                @"T:\github\MultiAccountCommerce\浏览器应用开源版\qrcode_523446917.jpg",
            };

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private void OpenQqGroupLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = QqGroupUrl,
                    UseShellExecute = true,
                });
                SetLabelText(_lblInfoSub, "已通过系统默认浏览器打开 QQ 群链接。");
            }
            catch (Exception ex)
            {
                SetLabelText(_lblInfoSub, $"打开 QQ 群链接失败：{ex.Message}");
            }
        }
    }
}
