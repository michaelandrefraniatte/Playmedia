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
namespace Playshot
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_Shown(object sender, EventArgs e)
        {
            this.pictureBox1.Dock = DockStyle.Fill;
            EO.WebEngine.BrowserOptions options = new EO.WebEngine.BrowserOptions();
            options.EnableWebSecurity = false;
            EO.WebBrowser.Runtime.DefaultEngineOptions.SetDefaultBrowserOptions(options);
            EO.WebEngine.Engine.Default.Options.AllowProprietaryMediaFormats();
            EO.WebEngine.Engine.Default.Options.SetDefaultBrowserOptions(new EO.WebEngine.BrowserOptions
            {
                EnableWebSecurity = false
            });
            this.webView1.Create(pictureBox1.Handle);
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
            using (System.IO.StreamReader file = new System.IO.StreamReader("colors.txt"))
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
            string path = @"playshot.txt";
            string readText = DecryptFiles(path + ".encrypted", "tybtrybrtyertu50727885");
            string folderpath = "file:///" + System.Reflection.Assembly.GetEntryAssembly().Location.Replace(@"file:\", "").Replace(Process.GetCurrentProcess().ProcessName + ".exe", "").Replace(@"\", "/").Replace(@"//", "") + "img/";
            readText = readText.Replace("file:///C:/Users/mic/Documents/GitHub/PS/Src/Playshot/Playshot/bin/Release/img/", folderpath);
            string oldobject = "'2019': ['IMG_20200828_131141.gif', 'IMG_20200828_131407.gif'], '2020': [], '2021': []";
            string newobject = CreateObject(folderpath.Replace("file:///", ""));
            readText = readText.Replace(oldobject, newobject).Replace("backgroundcolor", backgroundcolor).Replace("overlaycolor", overlaycolor).Replace("previousnextbuttonshovercolor", previousnextbuttonshovercolor).Replace("titlehoverbackgroundcolor", titlehoverbackgroundcolor);
            webView1.LoadHtml(readText);
        }
        public static string CreateObject(string targetDirectory)
        {
            string dir = "";
            string img = "";
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
                    img = fileName.Replace(subdirectory, "").Replace(@"\", "");
                    arraycreated += "'" + img + "', ";
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
            this.webView1.Dispose();
        }
    }
}
