using System.IO;
using System.Runtime.InteropServices;

namespace CotrollerDemo.Models
{
    public static class FileOperation
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string lpFileName);

        public static void Delete(this string directory)
        {
            if (Directory.Exists(directory))
            {
                var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    DeleteFile(file);
                }

                var directories = Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories);
                foreach (string dir in directories)
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}