using System.Drawing;
using System.Windows.Forms;

namespace TaskManager
{
    public static class ThemeManager
    {
        public static void ApplyTheme(Form form, bool isDarkMode)
        {
            Color backColor = isDarkMode ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
            Color foreColor = isDarkMode ? Color.White : SystemColors.ControlText;
            Color controlBack = isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
            Color controlFore = isDarkMode ? Color.White : SystemColors.WindowText;
            Color buttonBack = isDarkMode ? Color.FromArgb(85, 85, 85) : SystemColors.Control;

            form.BackColor = backColor;
            form.ForeColor = foreColor;
            ApplyThemeToControls(form.Controls, backColor, foreColor, controlBack, controlFore, buttonBack);
        }

        private static void ApplyThemeToControls(Control.ControlCollection controls, Color backColor, Color foreColor, Color controlBack, Color controlFore, Color buttonBack)
        {
            foreach (Control ctrl in controls)
            {
                if (ctrl is TextBox || ctrl is ListBox || ctrl is ComboBox || ctrl is NumericUpDown || ctrl is ListView || ctrl is DateTimePicker || ctrl is PropertyGrid)
                {
                    ctrl.BackColor = controlBack; ctrl.ForeColor = controlFore;
                }
                else if (ctrl is DataGridView)
                {
                    var dgv = (DataGridView)ctrl;
                    dgv.BackgroundColor = backColor; dgv.DefaultCellStyle.BackColor = controlBack; dgv.DefaultCellStyle.ForeColor = controlFore;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = backColor; dgv.ColumnHeadersDefaultCellStyle.ForeColor = foreColor;
                    dgv.GridColor = Color.Gray; dgv.EnableHeadersVisualStyles = false;
                }
                else if (ctrl is Button)
                {
                    var btn = (Button)ctrl;
                    btn.BackColor = buttonBack; btn.ForeColor = foreColor; btn.FlatStyle = FlatStyle.Flat;
                }
                else if (ctrl is Label || ctrl is CheckBox || ctrl is RadioButton || ctrl is GroupBox || ctrl is TabPage)
                {
                    ctrl.ForeColor = foreColor;
                }
                else if (ctrl is Panel || ctrl is SplitContainer || ctrl is TabControl)
                {
                    ctrl.BackColor = backColor; ctrl.ForeColor = foreColor;
                }
                if (ctrl.Controls.Count > 0) ApplyThemeToControls(ctrl.Controls, backColor, foreColor, controlBack, controlFore, buttonBack);
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
            if (priority == "高") return isColorVisionSupport ? Color.FromArgb(213, 94, 0) : Color.Red;
            if (priority == "中") return isColorVisionSupport ? Color.FromArgb(230, 159, 0) : Color.Orange;
            if (priority == "低") return isColorVisionSupport ? Color.FromArgb(0, 158, 115) : Color.Green;
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
