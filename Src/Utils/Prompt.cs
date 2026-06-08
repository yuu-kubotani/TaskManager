using System.Windows.Forms;

namespace UniConsul.Forms
{
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption, string defaultValue = "")
        {
            Form prompt = new Form()
            {
                Width = 400, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption, StartPosition = FormStartPosition.CenterParent
            };
            Label textLabel = new Label() { Left = 15, Top = 15, Text = text, AutoSize = true };
            TextBox textBox = new TextBox() { Left = 15, Top = 40, Width = 350, Text = defaultValue };
            Button confirmation = new Button() { Text = "OK", Left = 190, Top = 70, Width = 80, DialogResult = DialogResult.OK };
            Button cancel = new Button() { Text = "キャンセル", Left = 285, Top = 70, Width = 80, DialogResult = DialogResult.Cancel };
            
            prompt.Controls.Add(textLabel); prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation); prompt.Controls.Add(cancel);
            prompt.AcceptButton = confirmation; prompt.CancelButton = cancel;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }
    }
}
