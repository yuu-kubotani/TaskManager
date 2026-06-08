using System;
using System.Drawing;
using System.Windows.Forms;
using UniConsul.Services;
using System.Security.Cryptography;
using System.Text;

namespace UniConsul.Forms
{
    public class FormLogin : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly string _correctPasscode;
        private TextBox txtPasscode;

        public FormLogin(string correctPasscode, bool isDarkMode)
        {
            _correctPasscode = correctPasscode;
            InitializeComponent();
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

        private void InitializeComponent()
        {
            this.Text = "ログイン";
            this.Size = new Size(300, 160);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var lbl = new Label { Text = "パスコードを入力してください:", Location = new Point(10, 15), AutoSize = true };
            this.Controls.Add(lbl);

            txtPasscode = new TextBox { Location = new Point(10, 40), Size = new Size(260, 25), PasswordChar = '*' };
            this.Controls.Add(txtPasscode);

            var btnOk = new Button { Text = "OK", Location = new Point(90, 80), Size = new Size(80, 25) };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "キャンセル", Location = new Point(180, 80), Size = new Size(80, 25) };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Shown += (s, e) => txtPasscode.Focus();
        }

        private static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string inputHash = ComputeHash(txtPasscode.Text);
            // 新しいハッシュ化されたパスコード、または旧バージョンの平文パスコードと一致するか確認
            if (inputHash == _correctPasscode || txtPasscode.Text == _correctPasscode)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("パスコードが間違っています。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtPasscode.SelectAll();
                txtPasscode.Focus();
            }
        }
    }
}
