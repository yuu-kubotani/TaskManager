﻿﻿using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UniConsul.Native;

namespace UniConsul
{
    public static class ThemeManager
    {
        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static void ApplyTheme(Form form, bool isDarkMode)
        {
            // --- 全フォーム共通でアイコンを適用する ---
            try {
                string iconPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(iconPath)) {
                    form.Icon = new Icon(iconPath);
                } else {
                    string pngPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "uni consul（ユニコン）.png");
                    if (!System.IO.File.Exists(pngPath)) pngPath = @"C:\Users\kuyuu\OneDrive\デスクトップ\TaskManager\uni consul（ユニコン）.png";
                    if (System.IO.File.Exists(pngPath)) {
                        using (Bitmap originalBmp = new Bitmap(pngPath))
                        {
                            Bitmap bmp = new Bitmap(originalBmp.Width, originalBmp.Height);
                            using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); g.DrawImage(originalBmp, 0, 0); }
                            IntPtr hIcon = bmp.GetHicon();
                            form.Icon = Icon.FromHandle(hIcon);
                        }
                    }
                }
            } catch { }
            // ----------------------------------------

            Color backColor = isDarkMode ? Color.FromArgb(30, 30, 30) : SystemColors.Control; // 一番奥の背景（深い黒）
            Color foreColor = isDarkMode ? Color.White : SystemColors.ControlText;
            Color controlBack = isDarkMode ? Color.FromArgb(37, 37, 38) : SystemColors.Window; // 手前のコントロール（少し明るい黒）
            Color controlFore = isDarkMode ? Color.White : SystemColors.WindowText;
            Color buttonBack = isDarkMode ? Color.FromArgb(50, 50, 55) : SystemColors.Control;

            form.BackColor = backColor;
            form.ForeColor = foreColor;
            // フォーム自体（AutoScrollの画面）のスクロールバーもダークモード化する
            SetWindowTheme(form.Handle, isDarkMode ? "DarkMode_Explorer" : "Explorer", null);
            ApplyThemeToControls(form, form.Controls, backColor, foreColor, controlBack, controlFore, buttonBack, isDarkMode);
        }

        public static void ApplyDarkModeToWindow(System.IntPtr handle, bool isDarkMode)
        {
            try
            {
                int useImmersiveDarkMode = isDarkMode ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(handle, 20, ref useImmersiveDarkMode, sizeof(int));
                NativeMethods.DwmSetWindowAttribute(handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private static void ApplyThemeToControls(Form form, Control.ControlCollection controls, Color backColor, Color foreColor, Color controlBack, Color controlFore, Color buttonBack, bool isDarkMode)
        {
            string themeName = isDarkMode ? "DarkMode_Explorer" : "Explorer";
            
            foreach (Control ctrl in controls)
            {
                if (ctrl is ComboBox)
                {
                    SetWindowTheme(ctrl.Handle, isDarkMode ? "DarkMode_CFD" : "Explorer", null);
                }
                else if (ctrl is TabControl)
                {
                    var tc = (TabControl)ctrl;
                    SetWindowTheme(tc.Handle, "", "");
                    tc.Appearance = TabAppearance.Normal;
                }
                // スクロールバーを持つ可能性のあるコントロール（※DateTimePickerは白化の元凶になるため除外）
                else if (ctrl is DataGridView || ctrl is TextBox || ctrl is TextBoxBase || ctrl is ListBox || ctrl is ListView || ctrl is TreeView || ctrl is NumericUpDown || ctrl is ScrollBar || ctrl is Panel)
                {
                    SetWindowTheme(ctrl.Handle, themeName, null);
                }

                if (ctrl is Panel || ctrl is GroupBox || ctrl is SplitContainer || ctrl is SplitterPanel || ctrl is TableLayoutPanel || ctrl is FlowLayoutPanel)
                {
                    ctrl.BackColor = backColor;
                    ctrl.ForeColor = foreColor;
                }
                else if (ctrl is TextBox || ctrl is ComboBox || ctrl is NumericUpDown || ctrl is CheckedListBox)
                {
                    ctrl.BackColor = controlBack;
                    ctrl.ForeColor = foreColor;
                    if (ctrl is ComboBox) 
                    {
                        // FlatStyle が Flat だとネイティブのダークテーマが無視されて白くなるため System にする
                        ((ComboBox)ctrl).FlatStyle = isDarkMode ? FlatStyle.System : FlatStyle.Flat;
                    }
                    if (ctrl is TextBox) ((TextBox)ctrl).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (ctrl is DateTimePicker)
                {
                    var dtp = (DateTimePicker)ctrl;
                    // WinFormsの仕様上、DateTimePickerの入力欄はOSのシステムカラーで固定されるため設定を除外

                    UniConsul.Utils.UIUtility.ApplyDarkCalendar(dtp, form);
                }
                else if (ctrl is DataGridView)
                {
                    var dgv = (DataGridView)ctrl;
                    // リストのさらに裏側の余白部分をダーク背景色と同化させて白浮きを防ぐ
                    dgv.BackgroundColor = backColor;
                    dgv.DefaultCellStyle.BackColor = controlBack;
                    dgv.DefaultCellStyle.ForeColor = controlFore;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = backColor;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = foreColor;
                    dgv.GridColor = Color.Gray;
                    dgv.EnableHeadersVisualStyles = false;
                }
                else if (ctrl is Button)
                {
                    var btn = (Button)ctrl;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = isDarkMode ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                    btn.BackColor = isDarkMode ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                    if (btn.ForeColor != Color.Red) btn.ForeColor = foreColor;
                    else if (isDarkMode) btn.ForeColor = Color.LightCoral;
                }
                else if (ctrl is Label || ctrl is CheckBox || ctrl is RadioButton || ctrl is TabPage)
                {
                    ctrl.ForeColor = foreColor;
                    if (ctrl is TabPage) 
                    {
                        var tp = (TabPage)ctrl;
                        // OSのテーマ描画ロックを解除し、自前のダーク背景色を反映させる
                        tp.UseVisualStyleBackColor = false;
                        // カレンダーやリストの裏地（TabPage）をダーク背景色に統一
                        tp.BackColor = backColor;
                    }
                }
                else if (ctrl is ListBox || ctrl is ListView || ctrl is PropertyGrid)
                {
                    ctrl.BackColor = controlBack;
                    ctrl.ForeColor = controlFore;
                }

                if (ctrl.Controls.Count > 0)
                {
                    ApplyThemeToControls(form, ctrl.Controls, backColor, foreColor, controlBack, controlFore, buttonBack, isDarkMode);
                }
            }
        }

        public static Color GetOverdueColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(213, 94, 0); // Vermilion
            return isDarkMode ? Color.FromArgb(255, 100, 100) : Color.Red;
        }

        public static Color GetSundayColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(213, 94, 0);
            return isDarkMode ? Color.FromArgb(255, 120, 120) : Color.Red;
        }

        public static Color GetSaturdayColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(0, 114, 178);
            return isDarkMode ? Color.FromArgb(120, 150, 255) : Color.Blue;
        }

        public static Color GetEventColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(230, 159, 0); // Orange
            return isDarkMode ? Color.Orange : Color.DarkOrange;
        }

        public static Color GetProjectColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(0, 158, 115); // Green
            return isDarkMode ? Color.LightGreen : Color.Green;
        }

        public static Color GetTaskColor(bool isDarkMode, bool isColorVisionSupport)
        {
            if (isColorVisionSupport) return Color.FromArgb(86, 180, 233); // Sky Blue
            return isDarkMode ? Color.SkyBlue : Color.Blue;
        }

        public static Color GetPriorityColor(string priority, bool isDarkMode, bool isColorVisionSupport)
        {
            if (priority == "高") return isColorVisionSupport ? Color.FromArgb(213, 94, 0) : (isDarkMode ? Color.FromArgb(255, 100, 100) : Color.Red);
            if (priority == "中") return isColorVisionSupport ? Color.FromArgb(230, 159, 0) : (isDarkMode ? Color.FromArgb(255, 180, 50) : Color.Orange);
            if (priority == "低") return isColorVisionSupport ? Color.FromArgb(0, 158, 115) : (isDarkMode ? Color.FromArgb(100, 200, 100) : Color.Green);
            return isDarkMode ? Color.Silver : Color.DimGray;
        }

        public static Color GetProgressCompleteColor(bool isColorVisionSupport)
        {
            return isColorVisionSupport ? Color.FromArgb(0, 158, 115) : Color.MediumSeaGreen;
        }

        public static Color GetProgressIncompleteColor(bool isColorVisionSupport)
        {
            return isColorVisionSupport ? Color.FromArgb(86, 180, 233) : Color.SteelBlue;
        }

        public static void EnableDynamicResizing(Form form, Models.AppSettings settings, System.Action saveAction = null, SplitContainer splitContainer = null)
        {
            form.FormClosing += (s, e) => {
                if (settings != null) {
                    if (form.WindowState == FormWindowState.Normal) {
                        settings.WindowWidth = form.Width;
                        settings.WindowHeight = form.Height;
                        if (splitContainer != null) settings.MainSplitterDistance = splitContainer.SplitterDistance;
                    }
                    if (saveAction != null) saveAction.Invoke();
                }
            };
        }
    }
}
