﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using UniConsul.Native;

namespace UniConsul.Utils
{
    public static class UIUtility
    {
        public static async Task ShowSaveFeedbackAndClose(Form form, string message)
        {
            foreach (Control c in form.Controls) { var b = c as Button; if (b != null) b.Enabled = false; }
            var lbl = new Label { Text = message, Font = new Font("Meiryo UI", 9, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(180, 0, 0, 0), AutoSize = true, Padding = new Padding(10) };
            form.Controls.Add(lbl);
            lbl.Location = new Point((form.ClientSize.Width - lbl.Width) / 2, (form.ClientSize.Height - lbl.Height) / 2);
            lbl.BringToFront();
            await Task.Delay(1200);
            form.DialogResult = DialogResult.OK;
            form.Close();
        }

        public static void ApplyDarkCalendar(DateTimePicker dtp, Form form)
        {
            dtp.DropDown += (s, ev) => {
                if (form.BackColor.R >= 100) return; // ライトモードの場合は無視
                IntPtr hMonthCal = NativeMethods.SendMessage(dtp.Handle, NativeMethods.DTM_GETMONTHCAL, IntPtr.Zero, IntPtr.Zero);
                if (hMonthCal != IntPtr.Zero) {
                    // カレンダーのポップアップ部分にモダンなダークテーマを強制適用
                    NativeMethods.SetWindowTheme(hMonthCal, "DarkMode_Explorer", null);
                    int bg = ColorTranslator.ToWin32(Color.FromArgb(30, 30, 30));
                    int fg = ColorTranslator.ToWin32(Color.White);
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_BACKGROUND, (IntPtr)bg);
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_TEXT, (IntPtr)fg);
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_TITLEBK, (IntPtr)ColorTranslator.ToWin32(Color.FromArgb(45, 45, 48)));
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_TITLETEXT, (IntPtr)fg);
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_MONTHBK, (IntPtr)bg);
                    NativeMethods.SendMessage(hMonthCal, NativeMethods.MCM_SETCOLOR, (IntPtr)NativeMethods.MCSC_TRAILINGTEXT, (IntPtr)ColorTranslator.ToWin32(Color.Gray));
                }
            };
        }
    }
}