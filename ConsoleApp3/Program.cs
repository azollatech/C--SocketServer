using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;

namespace Server
{
    public class Program
    {
        static void Main(string[] args)
        {
            RowSocketServer myServer = new RowSocketServer();
            myServer.Start();
        }
    }
    public class RowSocketServer
    {
        public static string forwardDestination = "http://gps.racetimingsolutions.com/data-import-2"; // URL of forward destination for HTTP POST request
        private static Socket listener;
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public const int _bufferSize = 1024;
        public const int _port = 40000; // listening port
        public static bool _isRunning = true;
        public const char rawSpliter = '*'; // Spliter of raw data
        public const char contentSpliter = ','; // Spliter of content
        public static string[] commands = { "manufacturer", "serial_num", "version", "time" ,"S","latitude","D","longitude","G","speed","direction","date","vehicle_status"};
        class StateObject
        {
            public Socket workSocket = null;
            public byte[] buffer = new byte[_bufferSize];
            public StringBuilder sb = new StringBuilder();
        }

        // Returns the string between str1 and str2
        static string Between(string str, string str1, string str2)
        {
            int i1 = 0, i2 = 0;
            string rtn = "";

            i1 = str.IndexOf(str1, StringComparison.InvariantCultureIgnoreCase);
            if (i1 > -1)
            {
                i2 = str.IndexOf(str2, i1 + 1, StringComparison.InvariantCultureIgnoreCase);
                if (i2 > -1)
                {
                    rtn = str.Substring(i1 + str1.Length, i2 - i1 - str1.Length);
                }
            }
            return rtn;
        }

        static bool IsSocketConnected(Socket s)
        {
            return !((s.Poll(1000, SelectMode.SelectRead) && (s.Available == 0)) || !s.Connected);
        }

        public void Start()
        {

            IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _port);
            listener = new Socket(localEP.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(localEP);

            while (_isRunning)
            {
                allDone.Reset();
                listener.Listen(10); // The maximum length of the pending connections queue.
                listener.BeginAccept(new AsyncCallback(acceptCallback), listener);
                bool isRequest = allDone.WaitOne(new TimeSpan(12, 0, 0)); // Blocks for 12 hours

                if (!isRequest)
                {
                    allDone.Set();
                }
            }
            listener.Close();
        }

        static void acceptCallback(IAsyncResult ar)
        {
            // Get the listener that handles the client request.
            Socket listener = (Socket)ar.AsyncState;

            if (listener != null)
            {
                Socket handler = listener.EndAccept(ar);

                // Signal main thread to continue
                allDone.Set();

                // Create state
                StateObject state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, _bufferSize, 0, new AsyncCallback(readCallback), state);
            }
        }

        static void readCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            if (!IsSocketConnected(handler))
            {
                handler.Close();
                return;
            }

            int read = handler.EndReceive(ar);

            // Data was read from the client socket.
            if (read > 0)
            {
                state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, read));
                // Console.WriteLine(state.sb.ToString());
                string[] strings = (state.sb.ToString().Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries));
                for (int i = 0; i < strings.Length; i = i + 1)
                {
                    Console.WriteLine(strings[i]);
                    string outData = parseToJson(strings[i]); // Parse to JSON format
                    if (outData != "{}")
                        sendHTTPRequest(outData); // Send HTTP POST request
                }
                handler.BeginReceive(state.buffer, 0, _bufferSize, 0, new AsyncCallback(readCallback), state);
            }
            else
            {
                handler.Close();
            }
        }

        // Parse data to JSON format
        static string parseToJson(string raw)
        {
            // Console.WriteLine(raw);
            string[] datas = raw.Split(',');
            string outData = "{";
            if (datas.Length > commands.Length)
            {
                if (datas[2] == "V1" || datas[2] == "V2")
                {

                    for (int x = 0; x < commands.Length - 1; x++)
                    {
                        // Console.WriteLine(datas[x]);
                        if (x == 0)
                            outData += "\"" + commands[x] + "\":\"" + datas[x] + "\"";
                        else
                            outData += ",\"" + commands[x] + "\":\"" + datas[x] + "\"";
                    }
                }
            }
            else
            {
                // Malformed data
                // Console.WriteLine("Malformed data");
            }
            outData += "}";
            Console.WriteLine(outData);
            return outData;
        }

        // Send JSON data to destination via HTTP POST request
        public static void sendHTTPRequest(string json)
        {
            try
            {
                // Configuration of HTTP request
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(forwardDestination);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.ContentLength = json.Length;

                // Send HTTP POST request
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(json);
                    streamWriter.Flush();
                    streamWriter.Close();
                }


                /*
                 * Receive HTTP response
                 */
                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = streamReader.ReadToEnd();
                    Console.WriteLine(result);
                }
            }
            catch (WebException e)
            {
                Console.WriteLine("Exception Message :" + e.Message);
                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    Console.WriteLine("Status Code : {0}", ((HttpWebResponse)e.Response).StatusCode);
                    Console.WriteLine("Status Description : {0}", ((HttpWebResponse)e.Response).StatusDescription);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
