using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NFAuthenticationKey
{
    /* 
     * Copyright (C) 2020 Stefano Gottardo - @CastagnaIT
     * SPDX-License-Identifier: GPL-3.0-only
     * See LICENSES/GPL-3.0-only.md for more information.
     */
    public partial class MainWindow : Window
    {
        Thread operationsThread = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Initialized(object sender, EventArgs e)
        {
            TBVersion.Text = "Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString();

            // If needed extract files from incorporated resources
            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings.json")) == false)
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings.json"), Helper.GetFromResources("settings.json"));

            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "WaitingMessage.html")) == false)
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "WaitingMessage.html"), Helper.GetFromResources("WaitingMessage.html"));

            // Load settings from settings.json file
            try
            {
                JObject settings = JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings.json")));

                Helper.chromeExePath = Helper.GetChromeExePath(settings["chromeExePath"].ToString());
                Helper.url = settings["url"].ToString();
                Helper.localhostAddress = settings["localhostAddress"].ToString();
                Helper.chromeDebugPort = settings["chromeDebugPort"].ToObject<int>();
            }
            catch (Newtonsoft.Json.JsonReaderException exc)
            {
                if (exc.Message.Contains("escape sequence") && exc.Message.Contains("chromeExePath")) {
                    MessageBox.Show(this,
                        "The path set to chromeExePath property in the settings.json file has not been formatted correctly." + Environment.NewLine +
                        "It must be formatted as the following example:" + Environment.NewLine +
                        "C:\\\\mypath\\\\chrome.exe",
                        this.Title);
                    Close();
                }
                else
                {
                    MessageBox.Show(this, exc.Message + Environment.NewLine + Environment.NewLine + exc.ToString(), this.Title);
                    Close();
                }
            }
            catch (NFAuthException exc)
            {
                MessageBox.Show(this, exc.Message, this.Title);
                Close();
            }
            catch (Exception exc)
            {
                MessageBox.Show(this, exc.Message + Environment.NewLine + Environment.NewLine + exc.ToString(), this.Title);
                Close();
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Ask to user to close all Chrome windows
            if (Helper.IsChromeOpened(true))
            {
                MessageBox.Show(this, "Please close all Chrome windows opened", this.Title);
                return;
            }

            // Try close all Chrome opened in background, they can cause problems with our Chrome dev instance
            if (Helper.TerminateAllChromeInstances())
                Thread.Sleep(500);  // Give so time to the system

            // Not always we have the permission to terminate all Chrome instances, so check it
            if (Helper.IsChromeOpened())
            {
                MessageBox.Show(this, "There are some Chrome processes opened.\r\nCheck with the Task Manager and close them all", this.Title);
                return;
            }

            BtnCancel.IsEnabled = true;
            BtnStart.IsEnabled = false;
            operationsThread = new Thread(new ThreadStart(Operations));
            operationsThread.Start();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            BtnCancel.IsEnabled = false;
            BtnStart.IsEnabled = true;
            operationsThread.Abort();
            UpdateStatus("Not running");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Operations()
        {
            int chromePid = -1;
            try
            {
                WebSocketHelper.msgId = 0;

                // Open a Chrome dev instance in incognito mode
                UpdateStatus("Chrome startup... please wait");
                chromePid = Helper.OpenChromeInstance();

                // Wait for chrome opening and remote debugging service start-up
                UpdateStatus("Establish connection with Chrome... please wait");
                if (WebSocketHelper.WaitForPortOpened() == false)
                    throw new NFAuthException(string.Format("Unable communicate with Chrome debug address {0}:{1}", Helper.localhostAddress, Helper.chromeDebugPort));

                // Get endpoint of our page
                WebSocketHelper.ExtractDebugEndpoint();

                WebSocketHelper.OpenWebsocket();
                WebSocketHelper.WSRequest("Network.enable");
                WebSocketHelper.WSRequest("Page.enable");

                // Load the login webpage
                UpdateStatus("Opening login webpage... please wait");
                WebSocketHelper.WSRequest("Page.navigate", "{'url': '" + Helper.url + "'}");
                //string frameId = WebSocketHelper.WSRequest("Page.navigate", "{'url': '" + Helper.url + "'}")["result"]["frameId"].ToString();

                WebSocketHelper.WSWaitEvent("Page.domContentEventFired");  // Wait loading DOM (document.onDOMContentLoaded event)

                // Wait for the user to login 
                UpdateStatus("Please login in to website now ...waiting for you to finish...");
                if (Helper.WaitUserLoggedin() == false)
                    throw new NFAuthException("You have exceeded the time available for the login. Restart the operations.");

                WebSocketHelper.WSWaitEvent("Page.domContentEventFired");  // Wait loading DOM (document.onDOMContentLoaded event)

                // Verify that falcorCache data exist, this data exist only when logged
                UpdateStatus("Verification of data in progress... please wait");
                string htmlPage = WebSocketHelper.WSRequest("Runtime.evaluate", "{'expression': 'document.documentElement.outerHTML'}")["result"]["result"]["value"].ToString();
                if (htmlPage.Contains("falcorCache") == false)
                    throw new NFAuthException("Possible wrong login or unexpected problem, please try again.");

                WebSocketHelper.WSWaitEvent("Page.loadEventFired");  // Wait loading page (window.onload event)

                UpdateStatus("File creation in progress... please wait");

                // Get all cookies
                JArray cookies = WebSocketHelper.WSRequest("Network.getAllCookies")["result"]["cookies"].ToObject<JArray>();
                Helper.AssertCookies(cookies);

                // Generate a random PIN for access to "NFAuthentication.key" file
                string pin = new Random().Next(1000, 9999).ToString();

                // Create file data structure
                JObject data_content = new JObject();
                data_content["cookies"] = cookies;
                JObject data = new JObject();
                data["app_name"] = Assembly.GetExecutingAssembly().GetName().Name;
                data["app_version"] = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                data["app_system"] = "Windows";
                data["app_author"] = "CastagnaIT";
                data["timestamp"] = (int)DateTime.UtcNow.AddDays(5).Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                data["data"] = data_content;

                // Save the "NFAuthentication.key" file
                Helper.SaveData(data, pin);

                // Close the browser
                WebSocketHelper.WSRequest("Browser.close");

                string strMessage = "Operations completed!\r\nThe 'NFAuthentication.key' file has been saved in current folder.\r\nYour PIN protection is: " + pin;
                UpdateStatus(strMessage);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnCancel.IsEnabled = false;
                    BtnStart.IsEnabled = true;
                    MessageBox.Show(this, strMessage, this.Title);
                }), DispatcherPriority.Background);
            }
            catch (ThreadAbortException)
            {
                Helper.TerminateChromeInstance(chromePid);
            }
            catch (NFAuthException exc)
            {
                UpdateStatus(exc.Message);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnCancel.IsEnabled = false;
                    BtnStart.IsEnabled = true;
                    MessageBox.Show(this, exc.Message, this.Title);
                }), DispatcherPriority.Background);

                Helper.TerminateChromeInstance(chromePid);
            }
            catch (Exception exc)
            {
                UpdateStatus(exc.Message + Environment.NewLine + Environment.NewLine + exc.ToString());
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    BtnCancel.IsEnabled = false;
                    BtnStart.IsEnabled = true;
                    MessageBox.Show(this, exc.Message, this.Title);
                }), DispatcherPriority.Background);

                Helper.TerminateChromeInstance(chromePid);
            }
            finally
            {
                WebSocketHelper.CloseWebsocket();
            }
        }

        public void UpdateStatus(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TBStatus.Text = text;
            }), DispatcherPriority.Background);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (operationsThread != null && operationsThread.IsAlive)
            {
                try
                {
                    operationsThread.Abort();
                }
                catch (Exception)
                {
                }
            }
        }

    }
}
