using System;
using System.Drawing;
using System.Windows.Forms;
using UniConsul.Models;
using System.Linq;

namespace UniConsul.Forms
{
    public class FormAutoLogDetails : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public FormAutoLogDetails(AutoLogEntry log, string taskName, bool isDarkMode)
        {
            this.Text = "自動記録の詳細と判定スコア";
            this.Size = new Size(500, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var txtDetails = new TextBox
            {
                Dock = DockStyle.Top, Height = 350, Multiline = true,
                ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Meiryo UI", 9.5f)
            };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【基本情報】");
            sb.AppendLine("日時: " + log.Timestamp);
            sb.AppendLine("プロセス名: " + log.ProcessName);
            sb.AppendLine("ウィンドウタイトル: " + log.WindowTitle);
            sb.AppendLine("持続時間: " + log.DurationSeconds + " 秒");
            sb.AppendLine();
            sb.AppendLine("【推論結果】");
            sb.AppendLine("選定されたタスク/カテゴリ: " + taskName);

            if (log.Inference != null)
            {
                sb.AppendLine("合計スコア: " + log.Inference.TotalScore + " pt");
                sb.AppendLine();
                sb.AppendLine("【スコア加点の内訳】");
                if (log.Inference.ScoreDetails != null && log.Inference.ScoreDetails.Count > 0) {
                    foreach (var kvp in log.Inference.ScoreDetails.OrderByDescending(x => x.Value)) {
                        sb.AppendLine(string.Format(" +{0} pt : {1}", kvp.Value, kvp.Key));
                    }
                }
                else { sb.AppendLine(" (加点データなし)"); }
            }
            else { sb.AppendLine("推論データがありません (未分類)"); }

            txtDetails.Text = sb.ToString();
            txtDetails.Select(0, 0);
            
            var btnOk = new Button { Text = "閉じる", Size = new Size(100, 30), DialogResult = DialogResult.OK };
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            pnlBottom.Controls.Add(btnOk);
            btnOk.Location = new Point((this.ClientSize.Width - btnOk.Width) / 2, 10);

            this.Controls.Add(txtDetails);
            this.Controls.Add(pnlBottom);
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