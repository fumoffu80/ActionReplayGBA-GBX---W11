using System;
using System.Windows.Forms;

internal static class BootstrapProgram12314
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--self-test-language", StringComparison.OrdinalIgnoreCase))
                    return LanguageManager.SelfTest();
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        LanguageManager.Initialize();

        MainForm form = new MainForm();
        V12312ParityBridge.Attach(form);
        V1216VisualParity.Attach(form);
        V12314BoxArtFix.Attach(form);
        Application.Run(form);
        return 0;
    }
}
