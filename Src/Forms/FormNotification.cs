﻿﻿﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace UniConsul.Forms
{
    public class FormNotification : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public FormNotification(List<string> messages, bool isDarkMode)
        {
            this.Text = "🔔 リマインダー";
            this.Size = new Size(500, 350);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var lblHeader = new Label { Text = "以下のアイテムが期日を迎えます：", Location = new Point(10, 10), Font = new Font("Meiryo UI", 10, FontStyle.Bold), AutoSize = true };
            this.Controls.Add(lblHeader);

            var txtContent = new TextBox {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Location = new Point(10, 35), Size = new Size(465, 230),
                Text = string.Join("\r\n", messages),
                Font = new Font("Meiryo UI", 9)
            };
            this.Controls.Add(txtContent);

            var btnOk = new Button { Text = "閉じる", Location = new Point(200, 275), Size = new Size(80, 25), DialogResult = DialogResult.OK };
            this.Controls.Add(btnOk);
            this.AcceptButton = btnOk;

            ThemeManager.ApplyTheme(this, isDarkMode);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try {
                int useImmersiveDarkMode = this.BackColor.R < 100 ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            } catch { }
        }
    }
}
