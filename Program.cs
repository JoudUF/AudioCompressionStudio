using System;
using System.Windows.Forms;

namespace Multimedia
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing PixelLab UI Thread Engine...");
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run the main laboratory window
            Application.Run(new LabForm());
        }
    }
}