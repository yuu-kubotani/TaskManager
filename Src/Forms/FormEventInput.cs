using System;
using System.Drawing;
using System.Windows.Forms;
using TaskManager.Models;

namespace TaskManager.Forms
{
    public class FormEventInput : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private EventItem _existingEvent;
        public EventItem ResultEvent { get; private set; }

        private TextBox textTitle;
        private CheckBox checkAllDay;
        private DateTimePicker dateStart, timeStart;
        private DateTimePicker dateEnd, timeEnd;
        private Button buttonSave, buttonCancel;

        public FormEventInput(EventItem existingEvent, DateTime? defaultStartTime = null)
        {
            _existingEvent = existingEvent;
            InitializeComponent(defaultStartTime);
            LoadData();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool isDark = this.BackColor.R < 100;
            try {
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            bool isDark = this.BackColor.R < 100;
            try {
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
            Color formBg = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            Color surfaceBg = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
            Color fg = isDark ? Color.White : SystemColors.ControlText;

            this.BackColor = formBg;
            foreach (Control c in this.Controls) {
                var txt = c as TextBox;
                if (txt != null) { txt.BackColor = surfaceBg; txt.ForeColor = fg; txt.BorderStyle = BorderStyle.FixedSingle; }
                else {
                    var dtp = c as DateTimePicker;
                    if (dtp != null) { dtp.BackColor = surfaceBg; dtp.ForeColor = fg; }
                    else {
                        var btn = c as Button;
                        if (btn != null) {
                            btn.FlatStyle = FlatStyle.Flat;
                            btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                            btn.BackColor = isDark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                            btn.ForeColor = fg;
                        }
                        else if (c is Label || c is CheckBox) c.ForeColor = fg;
                    }
                }
            }
        }

        private void ApplyDarkCalendar(DateTimePicker dtp)
        {
            dtp.DropDown += (s, ev) => {
                if (this.BackColor.R >= 100) return; // ライトモードの場合は無視
                IntPtr hMonthCal = SendMessage(dtp.Handle, 0x1008, IntPtr.Zero, IntPtr.Zero);
                if (hMonthCal != IntPtr.Zero) {
                    SetWindowTheme(hMonthCal, "", "");
                    int bg = ColorTranslator.ToWin32(Color.FromArgb(30, 30, 30));
                    int fg = ColorTranslator.ToWin32(Color.White);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)0, (IntPtr)bg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)1, (IntPtr)fg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)2, (IntPtr)ColorTranslator.ToWin32(Color.FromArgb(45, 45, 48)));
                    SendMessage(hMonthCal, 0x1006, (IntPtr)3, (IntPtr)fg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)4, (IntPtr)bg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)5, (IntPtr)ColorTranslator.ToWin32(Color.Gray));
                }
            };
        }

        private void InitializeComponent(DateTime? defaultStartTime)
        {
            this.Text = _existingEvent != null ? "予定の編集" : "新しい予定";
            this.ClientSize = new Size(380, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            this.Controls.Add(new Label { Text = "タイトル：", Location = new Point(15, 15), AutoSize = true });
            textTitle = new TextBox { Location = new Point(15, 35), Size = new Size(350, 25) };
            this.Controls.Add(textTitle);

            checkAllDay = new CheckBox { Text = "終日の予定", Location = new Point(15, 75), AutoSize = true };
            checkAllDay.CheckedChanged += (s, e) => { timeStart.Enabled = !checkAllDay.Checked; timeEnd.Enabled = !checkAllDay.Checked; };
            this.Controls.Add(checkAllDay);

            this.Controls.Add(new Label { Text = "開始日時：", Location = new Point(15, 105), AutoSize = true });
            dateStart = new DateTimePicker { Location = new Point(15, 125), Width = 110, Format = DateTimePickerFormat.Short };
            timeStart = new DateTimePicker { Location = new Point(130, 125), Width = 80, Format = DateTimePickerFormat.Time, ShowUpDown = true };
            ApplyDarkCalendar(dateStart); ApplyDarkCalendar(timeStart);
            if (defaultStartTime.HasValue) { dateStart.Value = defaultStartTime.Value; timeStart.Value = defaultStartTime.Value; }
            this.Controls.Add(dateStart); this.Controls.Add(timeStart);

            this.Controls.Add(new Label { Text = "終了日時：", Location = new Point(220, 105), AutoSize = true });
            dateEnd = new DateTimePicker { Location = new Point(220, 125), Width = 110, Format = DateTimePickerFormat.Short };
            timeEnd = new DateTimePicker { Location = new Point(335, 125), Width = 80, Format = DateTimePickerFormat.Time, ShowUpDown = true };
            ApplyDarkCalendar(dateEnd); ApplyDarkCalendar(timeEnd);
            if (defaultStartTime.HasValue) { dateEnd.Value = defaultStartTime.Value.AddMinutes(30); timeEnd.Value = defaultStartTime.Value.AddMinutes(30); }
            dateEnd.Location = new Point(15, 155); timeEnd.Location = new Point(130, 155);
            this.Controls.Add(new Label { Text = "終了日時：", Location = new Point(15, 155), AutoSize = true, Visible = false }); // レイアウト調整用非表示
            this.Controls.Add(dateEnd); this.Controls.Add(timeEnd);

            buttonSave = new Button { Text = "保存", Location = new Point(200, 195), Size = new Size(80, 30) };
            buttonSave.Click += ButtonSave_Click;
            this.Controls.Add(buttonSave);

            buttonCancel = new Button { Text = "キャンセル", Location = new Point(290, 195), Size = new Size(80, 30) };
            buttonCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(buttonCancel);

            this.AcceptButton = buttonSave;
            this.CancelButton = buttonCancel;
        }

        private void LoadData()
        {
            if (_existingEvent != null)
            {
                textTitle.Text = _existingEvent.Title;
                checkAllDay.Checked = _existingEvent.IsAllDay;
                DateTime st;
                if (DateTime.TryParse(_existingEvent.StartTime, out st)) { dateStart.Value = st; timeStart.Value = st; }
                DateTime et;
                if (DateTime.TryParse(_existingEvent.EndTime, out et)) { dateEnd.Value = et; timeEnd.Value = et; }
            }
        }

        private async void ButtonSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DateTime startDt = dateStart.Value.Date;
            DateTime endDt = dateEnd.Value.Date;
            if (!checkAllDay.Checked)
            {
                startDt = startDt.Add(timeStart.Value.TimeOfDay);
                endDt = endDt.Add(timeEnd.Value.TimeOfDay);
            }

            if (endDt <= startDt && !checkAllDay.Checked)
            {
                MessageBox.Show("終了日時は開始日時より後に設定してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultEvent = _existingEvent ?? new EventItem { ID = Guid.NewGuid().ToString() };
            ResultEvent.Title = textTitle.Text.Trim();
            ResultEvent.IsAllDay = checkAllDay.Checked;
            ResultEvent.StartTime = startDt.ToString("o");
            ResultEvent.EndTime = endDt.ToString("o");

            await ShowSaveFeedbackAndClose("予定を保存しました");
        }

        private async System.Threading.Tasks.Task ShowSaveFeedbackAndClose(string message)
        {
            foreach (Control c in this.Controls) { var b = c as Button; if (b != null) b.Enabled = false; }
            var lbl = new Label { Text = message, Font = new Font("Meiryo UI", 9, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(180, 0, 0, 0), AutoSize = true, Padding = new Padding(10) };
            this.Controls.Add(lbl);
            lbl.Location = new Point((this.ClientSize.Width - lbl.Width) / 2, (this.ClientSize.Height - lbl.Height) / 2);
            lbl.BringToFront();
            await System.Threading.Tasks.Task.Delay(1200);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
