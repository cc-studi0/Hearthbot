using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartBot.Plugins.API;

namespace BotMain
{
    /// <summary>
    /// 将 SBAPI GuiElement 渲染为 WPF 控件到 Canvas 上
    /// </summary>
    public class GuiRenderer
    {
        private readonly Canvas _canvas;
        private int _lastVersion = -1;

        public GuiRenderer(Canvas canvas)
        {
            _canvas = canvas;
        }

        /// <summary>
        /// 检查版本变化并重新渲染
        /// </summary>
        public void Sync()
        {
            var ver = GuiBridge.Version;
            if (ver == _lastVersion) return;
            _lastVersion = ver;

            _canvas.Children.Clear();
            var elements = GuiBridge.GetElements();
            foreach (var el in elements)
                RenderElement(el);
        }

        private void RenderElement(GuiElement el)
        {
            if (el == null) return;

            try
            {
                if (el is GuiElementText txt)
                    RenderText(txt);
                else if (el is GuiElementButton btn)
                    RenderButton(btn);
                else if (el is GuiElementBitmap bmp)
                    RenderBitmap(bmp);
            }
            catch { }
        }

        private void RenderText(GuiElementText txt)
        {
            var color = txt.GetColor();
            var tb = new TextBlock
            {
                Text = txt.GetText() ?? "",
                FontSize = txt.GetFontSize() > 0 ? txt.GetFontSize() : 14,
                Foreground = new SolidColorBrush(color),
                Width = txt.GetWidth() > 0 ? txt.GetWidth() : double.NaN,
                Height = txt.GetHeight() > 0 ? txt.GetHeight() : double.NaN,
                TextWrapping = TextWrapping.Wrap
            };
            Place(tb, txt);
        }

        private void RenderButton(GuiElementButton btn)
        {
            var wpfBtn = new Button
            {
                Content = btn.GetText() ?? "",
                Width = btn.GetWidth() > 0 ? btn.GetWidth() : double.NaN,
                Height = btn.GetHeight() > 0 ? btn.GetHeight() : double.NaN,
                FontSize = 12
            };
            wpfBtn.Click += (_, _) =>
            {
                try { btn.TriggerOnClick(btn); } catch { }
            };
            Place(wpfBtn, btn);
        }

        private void RenderBitmap(GuiElementBitmap bmp)
        {
            var bytes = bmp.GetBitmapBytes();
            if (bytes == null || bytes.Length == 0) return;

            var bi = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
            }

            var img = new Image
            {
                Source = bi,
                Width = bmp.GetWidth() > 0 ? bmp.GetWidth() : double.NaN,
                Height = bmp.GetHeight() > 0 ? bmp.GetHeight() : double.NaN,
                Stretch = Stretch.Uniform
            };
            Place(img, bmp);
        }

        private void Place(UIElement control, GuiElement el)
        {
            Canvas.SetLeft(control, el.GetLeft());
            Canvas.SetTop(control, el.GetTop());
            _canvas.Children.Add(control);
        }
    }
}
