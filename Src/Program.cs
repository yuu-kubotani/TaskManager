using System;
using System.Windows.Forms;
using TaskManager.Forms;

namespace TaskManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // UIの見た目をモダン（OS標準）にするおまじない
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormMain());
        }
    }
}
