using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private static WebSocket websocket;
        private static ManualResetEvent WSopenedEvent = new ManualResetEvent(false);
        private static ManualResetEvent WSclosedEvent = new ManualResetEvent(false);

        private class DataResult
        {
            public int Id { get; set; }
            public string Method { get; set; }
            public JObject Data { get; set; }
            public ManualResetEvent MREvent { get; set; }
        }
        private static List<DataResult> dataResults = new List<DataResult>();

        public static string chromeDebugEndpoint = "";
        private static int _msgId = 0;
        public static int msgId { get
            {
                _msgId += 1;
                return _msgId;
            }
            set => _msgId = value;
        }


        public static void OpenWebsocket()
        {
            WSopenedEvent = new ManualResetEvent(false);
            WSclosedEvent = new ManualResetEvent(false);

            websocket = new WebSocket(chromeDebugEndpoint);
            websocket.MessageReceived += Websocket_MessageReceived;
            websocket.Opened += Websocket_Opened;
            websocket.Closed += Websocket_Closed;
            websocket.Error += Websocket_Error;

            websocket.Open();
            WSopenedEvent.WaitOne(TimeSpan.FromSeconds(5));

            dataResults.Clear();

            if (websocket.State != WebSocketState.Open)
                throw new NFAuthException("A problem prevented the opening of the websocket");
        }

        public static void CloseWebsocket()
        {
            try
            {
                if (websocket.State == WebSocketState.Open)
                {
                    websocket.Close();
                    WSclosedEvent.WaitOne();
                    websocket.Dispose();
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Websocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Debug.WriteLine("WEBSOCKET ERROR: " + e.Exception.Message);
        }

        private static void Websocket_Closed(object sender, EventArgs e)
        {
            WSclosedEvent.Set();
        }

        private static void Websocket_Opened(object sender, EventArgs e)
        {
            WSopenedEvent.Set();
        }

        private static void Websocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var dataReceived = JObject.Parse(e.Message);
            if (dataReceived.ContainsKey("id"))
            {
                var item = dataResults.FirstOrDefault(i => i.Id == dataReceived["id"].ToObject<int>());
                if (item != null)
                {
                    //Debug.Print(dataReceived.ToString());
                    item.Data = dataReceived;
                    item.MREvent.Set();
                }
            }
            else if (dataReceived.ContainsKey("method"))
            {
                var item = dataResults.FirstOrDefault(i => i.Method == dataReceived["method"].ToString());
                if (item != null)
                {
                    //Debug.Print(dataReceived.ToString());
                    item.Data = dataReceived;
                    item.MREvent.Set();
                }
            }
        }

        public static JObject WSRequest(string method, string parameters = "{}", int timeoutSecs = 10)
        {
            int id = msgId;
            string jsonRequest = string.Format("{{'id': {0}, 'method': '{1}', 'params': {2}}}", id, method, parameters);

            var item = new DataResult { Id = id, MREvent = new ManualResetEvent(false), Data = null };
            dataResults.Add(item);

            // Re-parse json text to make sure that we have a good json format
            websocket.Send(JObject.Parse(jsonRequest).ToString());

            item.MREvent.WaitOne(TimeSpan.FromSeconds(timeoutSecs));

            var dataCopy = item.Data;
            dataResults.Remove(item);

            return dataCopy;
        }

        public static JObject WSWaitEvent(string method, int timeoutSecs = 10)
        {
            var item = new DataResult { Id = -1, Method = method, MREvent = new ManualResetEvent(false), Data = null };
            dataResults.Add(item);

            item.MREvent.WaitOne(TimeSpan.FromSeconds(timeoutSecs));

            var dataCopy = item.Data;
            dataResults.Remove(item);

            return dataCopy;
        }

        /*
        /// <summary>
        /// Alternative method to "Page.Navigate"
        /// </summary>
        public static JObject WSRequestPageNavigate(string url)
        {
            return WSRequest("Runtime.evaluate", @"{""expression"":""document.location='" + url + @"'"",""objectGroup"":""console"",""includeCommandLineAPI"":true,""doNotPauseOnExceptions"":false,""returnByValue"":false}");
        }
        */

        public static void ExtractDebugEndpoint()
        {
            try
            {
                chromeDebugEndpoint = "";
                HttpWebRequest webReq = (HttpWebRequest)WebRequest.Create(string.Format("http://{0}:{1}/json", Helper.localhostAddress, Helper.chromeDebugPort));
                var responseStream = webReq.GetResponse().GetResponseStream();
                StreamReader sr = new StreamReader(responseStream);
                var response = sr.ReadToEnd();

                JArray sessionsList = JArray.Parse(response);
                foreach (var item in sessionsList)
                {
                    // Find our session page
                    if (item["type"].ToString() == "page" && item["url"].ToString().Contains("WaitingMessage.html"))
                    {
                        chromeDebugEndpoint = item["webSocketDebuggerUrl"].ToString();
                        Debug.WriteLine("CHROME SESSION: " + item.ToString());
                        break;
                    }
                }

                if (chromeDebugEndpoint == "")
                    throw new NFAuthException("Chrome session page not found");
            }
            catch (Exception)
            {
                throw;
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
