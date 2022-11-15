using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Drawing;
using System.ComponentModel;
using EO.WebBrowser;
using System.Media;
using System.Globalization;
using NetFwTypeLib;
namespace Playaudio
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        [DllImport("advapi32.dll")]
        private static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);
        [DllImport("user32.dll")]
        public static extern bool GetAsyncKeyState(System.Windows.Forms.Keys vKey);
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint ms);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint ms);
        [DllImport("ntdll.dll", EntryPoint = "NtSetTimerResolution")]
        public static extern void NtSetTimerResolution(uint DesiredResolution, bool SetResolution, ref uint CurrentResolution);
        public static uint CurrentResolution = 0;
        private void Form1_Shown(object sender, EventArgs e)
        {
            try
            {
                TimeBeginPeriod(1);
                NtSetTimerResolution(1, true, ref CurrentResolution);
                EO.WebEngine.Engine.Default.Options.AllowProprietaryMediaFormats();
                EO.WebEngine.Engine.Default.Options.SetDefaultBrowserOptions(new EO.WebEngine.BrowserOptions
                {
                    EnableWebSecurity = false
                });
                this.webView1.Create(pictureBox1.Handle);
                this.pictureBox1.Dock = DockStyle.Fill;
                this.webView1.Engine.Options.AllowProprietaryMediaFormats();
                this.webView1.SetOptions(new EO.WebEngine.BrowserOptions
                {
                    EnableWebSecurity = false
                });
                this.webView1.Engine.Options.DisableGPU = false;
                this.webView1.Engine.Options.DisableSpellChecker = true;
                this.webView1.Engine.Options.CustomUserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
                string backgroundcolor = "";
                string overlaycolor = "";
                string previousnextbuttonshovercolor = "";
                string titlehoverbackgroundcolor = "";
                using (StreamReader file = new StreamReader("colors.txt"))
                {
                    file.ReadLine();
                    backgroundcolor = file.ReadLine();
                    file.ReadLine();
                    overlaycolor = file.ReadLine();
                    file.ReadLine();
                    previousnextbuttonshovercolor = file.ReadLine();
                    file.ReadLine();
                    titlehoverbackgroundcolor = file.ReadLine();
                    file.Close();
                }
                string path = @"playaudio.txt";
                string readText = DecryptFiles(path + ".encrypted", "tybtrybrtyertu50727885");
                string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "") + "aud/";
                readText = readText.Replace("file:///C:/Users/mic/Documents/GitHub/playaudio/aud/", folderpath);
                string oldobject = "'Game': ['AnticheatingSolution.mp3', 'MonogameLearning.mp3'], 'Science': []";
                string newobject = CreateObject(folderpath.Replace("file:///", ""));
                readText = readText.Replace(oldobject, newobject).Replace("backgroundcolor", backgroundcolor).Replace("overlaycolor", overlaycolor).Replace("previousnextbuttonshovercolor", previousnextbuttonshovercolor).Replace("titlehoverbackgroundcolor", titlehoverbackgroundcolor);
                webView1.LoadHtml(readText);
            }
            catch
            {
                Application.Exit();
            }
        }
        public static string CreateObject(string targetDirectory)
        {
            string dir = "";
            string aud = "";
            string objectcreated = "";
            string arraycreated = "";
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
            {
                dir = subdirectory.Replace(targetDirectory, "");
                string[] fileEntries = Directory.GetFiles(subdirectory);
                arraycreated = "[";
                foreach (string fileName in fileEntries)
                {
                    aud = fileName.Replace(subdirectory, "").Replace(@"\", "");
                    if (aud.EndsWith(".mp3"))
                    {
                        arraycreated += "'" + aud + "', ";
                    }
                }
                arraycreated += "]";
                arraycreated = arraycreated.Replace(", ]", "]");
                objectcreated += "'" + dir + "': " + arraycreated + ", ";
            }
            objectcreated += "]";
            objectcreated = objectcreated.Replace(", ]", "");
            return objectcreated;
        }
        public static string DecryptFiles(string inputFile, string password)
        {
            using (var input = File.OpenRead(inputFile))
            {
                byte[] salt = new byte[8];
                input.Read(salt, 0, salt.Length);
                using (var decryptedStream = new MemoryStream())
                using (var pbkdf = new Rfc2898DeriveBytes(password, salt))
                using (var aes = new RijndaelManaged())
                using (var decryptor = aes.CreateDecryptor(pbkdf.GetBytes(aes.KeySize / 8), pbkdf.GetBytes(aes.BlockSize / 8)))
                using (var cs = new CryptoStream(input, decryptor, CryptoStreamMode.Read))
                {
                    string contents;
                    int data;
                    while ((data = cs.ReadByte()) != -1)
                        decryptedStream.WriteByte((byte)data);
                    decryptedStream.Position = 0;
                    using (StreamReader sr = new StreamReader(decryptedStream))
                        contents = sr.ReadToEnd();
                    decryptedStream.Flush();
                    return contents;
                }
            }
        }
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            TimeEndPeriod(1);
            this.webView1.Dispose();
        }
    }
}
