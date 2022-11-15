using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.IO;
using System.Management;
using System.Security.Cryptography;
namespace Playshot
{
    public static class Program
    {
        public static string username, hash, uniqueid, userchecksumuniqueid;
        const string alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
        private static bool authconnecting = false;
        /// <summary>
        /// Point d'entrée principal de l'application.
        /// </summary>
        [STAThread]
        public static void Main()
        {
            bool runelevated = false;
            bool oneinstanceonly = true;
            string filename = "la.txt";
            try
            {
                SetProcessPriority();
                if (oneinstanceonly)
                {
                    if (AlreadyRunning())
                    {
                        return;
                    }
                }
                if (runelevated)
                {
                    if (!hasAdminRights())
                    {
                        RunElevated();
                        return;
                    }
                }
                if (filename == "la.txt" & File.Exists("la.txt"))
                {
                    IsLocal();
                    if (!authconnecting)
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
            catch
            {
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
        public static bool hasAdminRights()
        {
            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static void RunElevated()
        {
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo();
                processInfo.Verb = "runas";
                processInfo.FileName = Application.ExecutablePath;
                Process.Start(processInfo);
            }
            catch { }
        }
        public static void IsLocal()
        {
            try
            {
                using (StreamReader file = new StreamReader("la.txt"))
                {
                    username = file.ReadLine();
                    hash = file.ReadLine();
                    file.Close();
                }
                String thisprocessname = Process.GetCurrentProcess().ProcessName;
                SHA1 sha1 = SHA1.Create();
                FileStream fs = new FileStream(thisprocessname + ".exe", FileMode.Open, FileAccess.Read);
                string checksum = BitConverter.ToString(sha1.ComputeHash(fs)).Replace("-", "");
                fs.Close();
                uniqueid = getUniqueId();
                userchecksumuniqueid = username + checksum + uniqueid;
                string salt = GetSalt(10);
                string hashedPass = HashPassword(salt, userchecksumuniqueid);
                if (hash != hashedPass)
                    authconnecting = false;
                else
                    authconnecting = true;
            }
            catch
            {
                authconnecting = false;
            }
        }
        public static string GetSalt(int saltSize)
        {
            float key = 0.6f;
            StringBuilder strB = new StringBuilder("");
            while ((saltSize--) > 0)
                strB.Append(alphanumeric[(int)(key * alphanumeric.Length)]);
            return strB.ToString();
        }
        public static string HashPassword(string salt, string password)
        {
            string mergedPass = string.Concat(salt, password);
            return EncryptUsingMD5(mergedPass);
        }
        public static string EncryptUsingMD5(string inputStr)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(inputStr));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                    sBuilder.Append(data[i].ToString("x2"));
                return sBuilder.ToString();
            }
        }
        public static string getUniqueId()
        {
            try
            {
                string cpuInfo = string.Empty;
                ManagementClass mc = new ManagementClass("win32_processor");
                ManagementObjectCollection moc = mc.GetInstances();
                foreach (ManagementObject mo in moc)
                {
                    cpuInfo = mo.Properties["processorID"].Value.ToString();
                    break;
                }
                string drive = "C";
                ManagementObject dsk = new ManagementObject(@"win32_logicaldisk.deviceid=""" + drive + @":""");
                dsk.Get();
                string volumeSerial = dsk["VolumeSerialNumber"].ToString();
                string uuidInfo = string.Empty;
                ManagementClass mcu = new ManagementClass("Win32_ComputerSystemProduct");
                ManagementObjectCollection mocu = mcu.GetInstances();
                foreach (ManagementObject mou in mocu)
                {
                    uuidInfo = mou.Properties["UUID"].Value.ToString();
                    break;
                }
                if (volumeSerial != null & volumeSerial != "" & cpuInfo != null & cpuInfo != "" & uuidInfo != null & uuidInfo != "")
                    return volumeSerial + "-" + cpuInfo + "-" + uuidInfo;
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }
        private static void SetProcessPriority()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.PriorityClass = ProcessPriorityClass.RealTime;
            }
        }
        private static bool AlreadyRunning()
        {
            String thisprocessname = Process.GetCurrentProcess().ProcessName;
            Process[] processes = Process.GetProcessesByName(thisprocessname);
            if (processes.Length > 1)
                return true;
            else
                return false;
        }
    }
}