using System;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            FBroRuntime.Initialize();
            try
            {
                new EcommerceWorkspaceSketchApp().Run();
            }
            finally
            {
                FBroRuntime.Shutdown();
            }
        }
    }
}
