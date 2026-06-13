using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UniConsul.Utils
{
    public static class IconHelper
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static void SetAppIcon(Form form)
        {
            try
            {
                var currentAsm = System.Reflection.Assembly.GetExecutingAssembly();
                string targetRes = currentAsm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("uni consul（ユニコン）.png"));
                if (!string.IsNullOrEmpty(targetRes))
                {
                    using (var strm = currentAsm.GetManifestResourceStream(targetRes))
                    using (var baseBmp = new Bitmap(strm))
                    {
                        var canvasBmp = new Bitmap(baseBmp.Width, baseBmp.Height);
                        using (var gr = Graphics.FromImage(canvasBmp))
                        {
                            gr.Clear(Color.Transparent);
                            gr.DrawImage(baseBmp, 0, 0);
                        }
                        var iconHandle = canvasBmp.GetHicon();
                        var winIcon = Icon.FromHandle(iconHandle);
                        form.Icon = winIcon;
                        form.FormClosed += (s, args) => {
                            if (winIcon != null) winIcon.Dispose();
                            DestroyIcon(iconHandle);
                            canvasBmp.Dispose();
                        };
                    }
                }
            }
            catch { }
        }
    }
}