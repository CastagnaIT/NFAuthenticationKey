using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using WebSocket4Net;

namespace NFAuthenticationKey
{
    /* 
     * Copyright (C) 2020 Stefano Gottardo - @CastagnaIT
     * SPDX-License-Identifier: GPL-3.0-only
     * See LICENSES/GPL-3.0-only.md for more information.
     */
    class WebSocketHelper
    {
        public static string chromeDebugEndpoint = "";
        private static int _msgId = 0;
        public static int msgId { get
            {
                _msgId += 1;
                return _msgId;
            }
            set => _msgId = value;
        }

        public static JObject WSRequest(string method, string parameters = "{}")
        {
            // Seem that too fast sequentials calls cause problems, e.g. web page is not loaded
            Thread.Sleep(500);

            string jsonRequest = string.Format("{{'id': {0}, 'method': '{1}', 'params': {2}}}", msgId, method, parameters);

            // Re-parse json text to make sure that we have a good json format
            return JObject.Parse(RequestData(JObject.Parse(jsonRequest).ToString()));
        }

        public static void ExtractDebugEndpoint()
        {
            using (var webClient = new WebClient())
            {
                string response = webClient.DownloadString(String.Format("http://{0}:{1}/json", Helper.localhostAddress, Helper.chromeDebugPort)).Replace("\r\n","");
                try
                {
                    chromeDebugEndpoint = JArray.Parse(response)[0]["webSocketDebuggerUrl"].ToString();
                    Console.WriteLine("CHROME WEB SOCKET ENDPOINT: " + chromeDebugEndpoint);
                }
                catch (Exception)
                {
                    throw new NFAuthException("Could not extract debug URL from debugger service");
                }
            }
        }

        public static string RequestData(string data)
        {
            string result = string.Empty;
            bool datareceived = false;
            
            using (WebSocket websocket = new WebSocket(chromeDebugEndpoint))
            {
                websocket.MessageReceived += new EventHandler<MessageReceivedEventArgs>(websocket_MessageReceived);
                websocket.Opened += new EventHandler(websocket_Opened);
                websocket.Open();
                TimeSpan maxDuration = TimeSpan.FromSeconds(10); // Timeout
                Stopwatch SW = Stopwatch.StartNew();
                while (!datareceived && SW.Elapsed < maxDuration)
                {
                    Thread.Sleep(100);
                }
                return result;
            }
           
            void websocket_Opened(object sender, EventArgs e)
            {
                (sender as WebSocket).Send(data);
            }

            void websocket_MessageReceived(object sender, MessageReceivedEventArgs e)
            {
                result = e.Message;
                datareceived = true;
            }
        }

        public static bool WaitForPortOpened()
        {
            TimeSpan maxDuration = TimeSpan.FromSeconds(15); // Timeout
            Stopwatch SW = Stopwatch.StartNew();
            while (SW.Elapsed < maxDuration)
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    try
                    {
                        string localhost = Helper.localhostAddress == "localhost" ? "127.0.0.1" : Helper.localhostAddress;
                        tcpClient.Connect(localhost, Helper.chromeDebugPort);
                        return true;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(200);
                    }
                }
            }
            return false;
        }
    }
}
