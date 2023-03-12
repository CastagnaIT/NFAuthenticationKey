using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        public static string browserExeName = ""; // executable name without extension
        public static string browserExePath = "";
        public static int browserDebugPort = 9222;
        public static string outputFileName = "NFAuthentication.key";


        public static string GetBrowserExePath(string browserName, string partialPath)
        {
            // First try to get the browser path by check for browser process name
            Process[] browserProcesses = Process.GetProcessesByName(browserName);
            if (browserProcesses.Length > 0)
            {
                return browserProcesses[0].MainModule.FileName;
            }
            else
            {
                // Try check the default paths
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), partialPath);
                if (File.Exists(path))
                    return path;

                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), partialPath);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        public static int OpenBrowserInstance()
        {
            using (Process browserProcess = new Process())
            {
                // Start the browser in incognito mode, to exclude custom extensions and to avoid malicious users
                browserProcess.StartInfo.UseShellExecute = false;
                browserProcess.StartInfo.FileName = browserExePath;
                string userDataPath = Path.Combine(Directory.GetCurrentDirectory(), "BrowserTempData");
                string htmlMessageFilePath = Path.Combine(Directory.GetCurrentDirectory(), "WaitingMessage.html");
                browserProcess.StartInfo.Arguments = String.Format("\"{0}\" --incognito --user-data-dir=\"{1}\" --remote-debugging-port={2} --remote-allow-origins=* --no-first-run --no-default-browser-check", htmlMessageFilePath, userDataPath, browserDebugPort);
                browserProcess.Start();
                return browserProcess.Id;
            }
        }

        public static bool IsBrowserOpened()
        {
            return IsBrowserOpened(false);
        }

        public static bool IsBrowserOpened(bool checkOnlyWithWindows)
        {
            if (checkOnlyWithWindows)
            {
                Process[] processes = Process.GetProcessesByName(browserExeName);
                return processes.Count(process => string.IsNullOrEmpty(process.MainWindowTitle) == false) > 0;
            }
            else
            {
                return Process.GetProcessesByName(browserExeName).Length > 0;
            }
        }

        public static bool TerminateAllBrowserInstances()
        {
            Process[] processes = Process.GetProcessesByName(browserExeName);
            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error killing browser process with pid {0}: {1}", process.Id, e);
                }

            }
            return processes.Length > 0;
        }

        public static void TerminateBrowserInstance(int pid)
        {
            if (pid <= 0)
                return;
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error killing browser process with pid {0}: {1}", pid, e);
            }
        }

        public static bool WaitUserLoggedin()
        {
            TimeSpan maxDuration = TimeSpan.FromMinutes(5); // Timeout
            Stopwatch SW = Stopwatch.StartNew();
            while (SW.Elapsed < maxDuration)
            {
                JObject historyData = WebSocketHelper.WSRequest("Page.getNavigationHistory", "{}", 5);
                if (historyData != null)
                {
                    int historyIndex = historyData["result"]["currentIndex"].ToObject<int>();

                    // If the current page url is like "https://www.n*****x.com/browse" means that the user should have logged in successfully
                    if (historyData["result"]["entries"][historyIndex]["url"].ToString().Contains("/browse"))
                        return true;
                }
                Thread.Sleep(500);
            }
            return false;
        }

        public static void AssertCookies(JArray cookies)
        {
            if (cookies.Count == 0)
                throw new NFAuthException("Not found cookies");

            List<string> loginCookies = new List<string> { "nfvdid", "SecureNetflixId", "NetflixId" };
            foreach (string cookieName in loginCookies)
            {
                if (cookies.Children<JObject>().FirstOrDefault(o => o["name"].ToString() == cookieName) == null)
                    throw new NFAuthException("Not found cookies");
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

        public static JObject ExtractJson(string content, string varName)
        {
            try
            {
                string pattern = @"netflix\.{}\s*=\s*(.*?);\s*<\/script>";
                var match = Regex.Match(content, pattern.Replace("{}", varName));
                if (match.Success)
                {
                    string jsonString = match.Groups[1].Value;

                    jsonString = jsonString.Replace("\\\"", "\\\\\""); // Escape \"
                    jsonString = jsonString.Replace(@"\s", @"\\s"); // Escape whitespace
                    jsonString = jsonString.Replace(@"\r", @"\\r"); // Escape return
                    jsonString = jsonString.Replace(@"\n", @"\\n"); // Escape line feed
                    jsonString = jsonString.Replace(@"\t", @"\\t"); // Escape tab
                    jsonString = jsonString.Replace(@"\p", @"/p"); // Unicode property not supported, we change slash to avoid unescape it

                    jsonString = Regex.Unescape(jsonString); // Unescape unicode string

                    return JsonConvert.DeserializeObject<JObject>(jsonString);
                }
                return null;
            }
            catch (Exception exc)
            {
                Console.WriteLine(string.Format("ExtractJson error {0} Unable to extract {1} data", exc, varName));
                return null;
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
