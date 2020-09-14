using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NFAuthenticationKey
{
    /* 
     * Copyright (C) 2020 Stefano Gottardo - @CastagnaIT
     * SPDX-License-Identifier: GPL-3.0-only
     * See LICENSES/GPL-3.0-only.md for more information.
     */
    class Helper
    {
        public static string url = "";
        public static string localhostAddress = "";
        public static string chromeExePath = "";
        public static int chromeDebugPort = 9222;
        public static string outputFileName = "NFAuthentication.key";


        public static string GetChromeExePath(string chromeCustomPath)
        {
            string path;

            if (chromeCustomPath.Contains("*"))
                chromeCustomPath = null;

            if (chromeCustomPath != null)
            {
                path = chromeCustomPath;
                if (File.Exists(path) == false)
                    throw new Exception("The Chrome browser executable path in the settings.json is wrong");
            }
            else
            {
                // First try to get the Chrome path by reading a Chrome process
                Process[] ChromeProcesses = Process.GetProcessesByName("chrome");
                if (ChromeProcesses.Length > 0)
                {
                    path = ChromeProcesses[0].MainModule.FileName;
                }
                else
                {
                    // Try use the default path
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe");
                }
                if (File.Exists(path) == false)
                    throw new Exception("The Chrome browser executable path has not been found.\r\nPlease specify it manually in the settings.json");
            }
            return path;
        }

        public static int OpenChromeInstance()
        {
            using (Process chromeProcess = new Process())
            {
                // Start Chrome in incognito mode, to exclude custom extensions and to avoid malicious users
                chromeProcess.StartInfo.UseShellExecute = false;
                chromeProcess.StartInfo.FileName = chromeExePath;
                string userDataPath = Path.Combine(Directory.GetCurrentDirectory(), "BrowserTempData");
                string htmlMessageFilePath = Path.Combine(Directory.GetCurrentDirectory(), "WaitingMessage.html");
                chromeProcess.StartInfo.Arguments = String.Format("\"{0}\" -incognito --user-data-dir=\"{1}\" --remote-debugging-port={2} --no-first-run --no-default-browser-check", htmlMessageFilePath, userDataPath, chromeDebugPort);
                chromeProcess.Start();
                return chromeProcess.Id;
            }
        }

        public static bool IsChromeOpened()
        {
            return Process.GetProcessesByName("chrome").Length > 0;
        }

        public static bool TerminateAllChromeInstances()
        {
            Process[] processes = Process.GetProcessesByName("chrome");
            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error killing Chrome process with pid {0}: {1}", process.Id, e);
                }

            }
            return processes.Length > 0;
        }

        public static void TerminateChromeInstance(int pid)
        {
            if (pid <= 0)
                return;
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error killing Chrome process with pid {0}: {1}", pid, e);
            }
        }

        public static bool WaitUserLoggedin()
        {
            TimeSpan maxDuration = TimeSpan.FromMinutes(5); // Timeout
            Stopwatch SW = Stopwatch.StartNew();
            while (SW.Elapsed < maxDuration)
            {
                JObject historyData = WebSocketHelper.WSRequest("Page.getNavigationHistory");
                int historyIndex = historyData["result"]["currentIndex"].ToObject<int>();

                // If the current page url is like "https://www.n*****x.com/browse" means that the user should have logged in successfully
                if (historyData["result"]["entries"][historyIndex]["url"].ToString().Contains("/browse"))
                    return true;
                Thread.Sleep(500);
            }
            return false;
        }

        public static void AssertCookies(JArray cookies)
        {
            if (cookies.Count == 0)
                throw new Exception("Not found cookies");

            List<string> loginCookies = new List<string> { "memclid", "nfvdid", "SecureNetflixId", "NetflixId" };
            foreach (string cookieName in loginCookies)
            {
                if (cookies.Children<JObject>().FirstOrDefault(o => o["name"].ToString() == cookieName) == null)
                    throw new Exception("Not found cookies");
            };
        }

        public static void SaveData(JObject data, string pin)
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), outputFileName);

            File.WriteAllText(filePath, EncryptDataAES(pin, data.ToString()));
        }
        
        public static string GetFromResources(string resourceName)
        {
            Assembly assem = Assembly.GetExecutingAssembly();

            using (Stream stream = assem.GetManifestResourceStream(assem.GetName().Name + "." + resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string EncryptDataAES(string key, string plainText)
        {
            byte[] iv = new byte[16];
            byte[] array;

            using (Aes aes = Aes.Create())
            {
                /*
                // Function to ensure 16 bytes length in the key
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] safeKeyBytes = new byte[16];
                int len = keyBytes.Length;
                if (len > safeKeyBytes.Length)
                {
                    len = safeKeyBytes.Length;
                }
                Array.Copy(keyBytes, safeKeyBytes, len);
                */
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.UTF8.GetBytes(key + key + key + key); // The key must have 16 byte
                aes.IV = iv;  // Set as bytes null

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream((Stream)memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter streamWriter = new StreamWriter((Stream)cryptoStream))
                        {
                            streamWriter.Write(plainText);
                        }

                        array = memoryStream.ToArray();
                    }
                }
            }

            return Convert.ToBase64String(array);
        }
    }
}
