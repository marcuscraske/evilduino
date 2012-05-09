/*
 * Creative Commons Attribution-ShareAlike 3.0 unported
 * *******************************************************************
 * Author:  limpygnome
 * E-mail:  limpygnome@gmail.com
 * Site:    ubermeat.co.uk
 * *******************************************************************
 * Credit to:
 * --   Netduino.com community for a lot of help <3
 * 
 * To-do:
 * --   Make a database class for a txt file, where data is written on
 *      lines. The idea would be to write data to the bottom but read
 *      it from bottom to top - opposed to top to bottom. This would
 *      be used for the guestbook posts system.
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.NetduinoPlus;
using System.Text;
using System.IO;
using System.Collections;

namespace BinaryClock
{
    public class Program
    {
        #region "Enums"
        public enum LightsMode
        {
            Time,
            AllOn,
            AllOff,
            Red,
            Green,
            Blue
        }
        #endregion

        #region "Constants"
        public const String github = "https://github.com/ubermeat/evilduino";
        public const String logClock = "SD\\Log_Clock.txt";
        public const String logWebserver = "SD\\Log_Webserver.txt";
        #endregion

        #region "Variables"
        public static DateTime currentTime = new DateTime();
        public static bool timeNeedsUpdating = false;
        public static bool testMode = false;
        public static Logger log;
        public static float temperature = 0;
        public static float light = 0;
        public static float tilt = 0;
        public static LightsMode lightsMode = LightsMode.Blue;
        public static LightsMode lightsModeDark = LightsMode.AllOff;
        public static LightsMode lightsModeLight = LightsMode.Time;
        public static LightsMode lightsModeAlarmBuzzA = LightsMode.Time;
        public static LightsMode lightsModeAlarmBuzzB = LightsMode.Green;
        public static bool lightsModeLocked = false; // Used to stop autonomous changes to lighting for e.g. error messages etc
        public static int lightModeDarkThreshold = 25;
        public static DateTime alarm = DateTime.MaxValue;
        #endregion

        public static void Main()
        {
            // Initialize logging
            log = new Logger(logClock);
            log.writeSeparator();
            log.write("Initialized logger...");
            // Grab the latest time
            updateTime();
            // Launch secondary thread to handle the lighting
            Thread lighting = new Thread(new ThreadStart(lightsThread));
            lighting.Start();
            // Launch the third thread to handle time updating
            Thread timeUpdating = new Thread(new ThreadStart(timeUpdatingThread));
            timeUpdating.Start();
            // Launch fourth thread to handle calculations and sensory mining
            Thread calculations = new Thread(new ThreadStart(calculationsThread));
            calculations.Start();
            // Launch the fifth thread for the webserver
            Thread webServer = new Thread(new ThreadStart(Webserver.initServer));
            webServer.Start();
            // Launch the sixth thread for the alarm
            Thread alarm = new Thread(new ThreadStart(alarmThread));
            alarm.Start();
            // Update the time
            updateTime();
            // Update the time locally until we need to resync
            int iterations = 0;
            const int updateIterationCount = 600; // Update every 10 minutes
            while (true)
            {
                iterations++;
                if (iterations >= updateIterationCount)
                {
                    iterations = 0;
                    timeNeedsUpdating = true;
                }
                currentTime = currentTime.AddSeconds(1); // Update the current time
                Thread.Sleep(1000);
            }
        }
        public static void alarmThread()
        {
            OutputPort buzzer = new OutputPort(Pins.GPIO_PIN_D6, false);
            while (true)
            {
                try
                {
                    // Check if the alarm time has been surpassed
                    if (currentTime >= alarm)
                    {
                        // Reset the alarm
                        alarm = DateTime.MaxValue;
                        // Sound the buzzer for two minutes
                        bool canFlash = !lightsModeLocked;
                        if (canFlash) lightsModeLocked = true;
                        for (int i = 0; i < 120; i++)
                        {
                            buzzer.Write(i % 2 == 0);
                            if (canFlash) lightsMode = (i % 2 == 0 ? lightsModeAlarmBuzzA : lightsModeAlarmBuzzB);
                            Thread.Sleep(1000);
                        }
                        if (canFlash)
                        {
                            lightsModeLocked = false;
                            lightsMode = LightsMode.Time;
                        }
                    }
                }
                catch(Exception ex)
                {
                    log.write("Critical malfunction occurred in alarmThread '" + ex.Message + "'!");
                }
                Thread.Sleep(1000);
            }
        }
        public static void timeUpdatingThread()
        {
            while (true)
            {
                if (timeNeedsUpdating)
                {
                    timeNeedsUpdating = false;
                    updateTime();
                }
                Thread.Sleep(100);
            }
        }
        static bool[] valueToBooleanBinary(int value, int length)
        {
            int mask = (int)System.Math.Pow(2, length - 1);
            bool[] values = new bool[length];
            for (int i = length - 1; i >= 0 && mask > 0; i--)
            {
                if ((value & mask) != 0) values[i] = true;
                mask >>= 1;
            }
            return values;
        }
        public static void calculationsThread()
        {
            // Sensors
            SecretLabs.NETMF.Hardware.AnalogInput sensorLight = new SecretLabs.NETMF.Hardware.AnalogInput(Pins.GPIO_PIN_A0);
            SecretLabs.NETMF.Hardware.AnalogInput sensorTemp = new SecretLabs.NETMF.Hardware.AnalogInput(Pins.GPIO_PIN_A1);
            SecretLabs.NETMF.Hardware.AnalogInput sensorTilt = new SecretLabs.NETMF.Hardware.AnalogInput(Pins.GPIO_PIN_A2);
            // Time
            bool[] hour;
            bool[] min;
            bool[] sec;
            int ta;
            int tb;
            int second;
            while (true)
            {
                try
                {
                    // Update sensor data
                    temperature = sensorTemp.Read() / 10; // Probably inaccurate...due to using 5v.../10 is good :D
                    light = (float)System.Math.Round(((float)sensorLight.Read() / 1023.0f) * 100.0f);
                    tilt = ((float)sensorTilt.Read() / 1023.0f);
                    // Update overall lighting based on light threshold
                    if (!lightsModeLocked)
                    {
                        if (light <= lightModeDarkThreshold)
                        { if (lightsMode != lightsModeDark) lightsMode = lightsModeDark; }
                        else if (lightsMode != lightsModeLight) lightsMode = lightsModeLight;
                    }
                    // Update lights based on mode
                    second = currentTime.Second;
                    switch (lightsMode)
                    {
                        case LightsMode.Time:
                            hour = valueToBooleanBinary(currentTime.Hour, 5);
                            min = valueToBooleanBinary(currentTime.Minute, 6);
                            sec = valueToBooleanBinary(second, 6);
                            ta = tb = 0;
                            // -- Hours
                            if (hour[0]) ta += 64;
                            if (hour[1]) ta += 128;
                            if (hour[2]) ta += 2;
                            if (hour[3]) ta += 8;
                            if (hour[4]) tb += 4;
                            // -- Mins
                            if (min[0]) tb += 2;
                            if (min[1]) ta += 1;
                            if (min[2]) tb += 1;
                            if (min[3]) tb += 16;
                            if (min[4]) ta += 16;
                            if (min[5]) ta += 4;
                            // -- Secs
                            if (sec[0]) tb += 128;
                            if (sec[1]) tb += 64;
                            if (sec[2]) tb += 32;
                            totalD7 = sec[3];
                            if (sec[4]) tb += 8;
                            if (sec[5]) ta += 32;
                            totalA = ta;
                            totalB = tb;
                            break;
                        case LightsMode.AllOff:
                            totalA = 0;
                            totalB = 0;
                            totalD7 = false;
                            break;
                        case LightsMode.AllOn:
                            totalA = 255;
                            totalB = 255;
                            totalD7 = true;
                            break;
                        case LightsMode.Blue:
                            totalA = 72;
                            totalB = 146;
                            totalD7 = true;
                            break;
                        case LightsMode.Green:
                            totalA = 145;
                            totalB = 76;
                            totalD7 = false;
                            break;
                        case LightsMode.Red:
                            totalA = 38;
                            totalB = 33;
                            totalD7 = false;
                            break;
                    }
                    // Sleep....until the time changes
                    while (currentTime.Second == second) Thread.Sleep(50);
                }
                catch(Exception ex)
                {
                    log.write("Critical malfunction occurred in calculationsThread: '" + ex.Message + "'!");
                    Thread.Sleep(1000);
                }
            }
        }
        static bool totalD7 = false;    // Pin 7 light
        static int totalA = 194;        // Shift register A
        static int totalB = 4;          // Shift register B
        public static void lightsThread()
        {
            OutputPort d7 = new OutputPort(Pins.GPIO_PIN_D7, false);
            Cpu.Pin latch = Pins.GPIO_PIN_D10;
            // http://forums.netduino.com/index.php?/topic/921-quick-simple-shift-register-example/page__p__6593__hl__shift+register__fromsearch__1#entry6593
            SPI.Configuration spiConfig = new SPI.Configuration(
                latch,
                false, // active state
                0,     // setup time
                0,     // hold time
                false, // clock idle state
                true,  // clock edge
                1000,   // clock rate
                SPI.SPI_module.SPI1);
            SPI dp = new SPI(spiConfig);
            while (true)
            {
                try
                {
                    dp.Write(new byte[] { (byte)totalA, (byte)totalB });
                    d7.Write(totalD7);
                }
                catch(Exception ex)
                {
                    log.write("Critical malfunction occurred in lightsThread '" + ex.Message + "'!");
                }
                Thread.Sleep(1000);
            }
        }
        public static void updateTime()
        {
            try
            {
                string l = latestTime();
                //Debug.Print("Latest time is: " + l);
                if (l == null) return;
                // Split the date up
                int year = int.Parse(new string(new char[] { l[0], l[1], l[2], l[3] }));
                int month = int.Parse(new string(new char[] { l[5], l[6] }));
                int day = int.Parse(new string(new char[] { l[8], l[9] }));
                int hour = int.Parse(new string(new char[] { l[11], l[12] })); ;
                int minute = int.Parse(new string(new char[] { l[14], l[15] })); ;
                int second = int.Parse(new string(new char[] { l[17], l[18] })); ;
                //Debug.Print("Parsed time is: " + day + "/" + month + "/" + year + " " + hour + ":" + minute + ":" + second);
                // Parse into datetime object
                DateTime dt = new DateTime(year, month, day, hour, minute, second, 0);
                // Check if to apply BST (+1 hour - begins: last Sunday of March -- ends: last Sunday of October)
                // -- 31 days in both March and October -- this method isnt entirely accurate, but it's good enough <3
                if (dt.Month > 3 && dt.Month < 10 || (dt.Month == 3 || dt.Month == 10) && dt.Day > 24)
                    // march 31 days, october 31 days
                    dt = dt.AddHours(1.0);
                currentTime = dt;
                log.write("Successfully updated the time to '" + currentTime.ToString("dd/MM/yyyy HH:mm:ss") + "'.");
                // Reset the lighting - but only if red i.e. an error occurred
                lightsMode = LightsMode.Time;
                if(lightsModeLocked && lightsMode == LightsMode.Red) lightsModeLocked = false;
            }
            catch (Exception ex)
            {
                lightsModeLocked = true;
                lightsMode = LightsMode.Red;
                log.write("Failed to update the time - '" + ex.Message + "'!");
            }
        }
        public static string latestTime()
        {
            // http://www.timeapi.org/gmt/now
            Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // Connect to the host
            sock.ReceiveTimeout = 1000;
            sock.SendTimeout = 1000;
            sock.Connect(new IPEndPoint(Dns.GetHostEntry("www.timeapi.org").AddressList[0], 80));
            //Debug.Print("Past connection phase");
            // Send the request
            sock.Send(Encoding.UTF8.GetBytes(
"GET /gmt/now HTTP/1.1\r\n"+
"Host: www.timeapi.org\r\n" +
"User-Agent: binaryclock\r\n"+
"\r\n"));
            int failedAttempts = 0;
            while (sock.Available <= 0 && failedAttempts < 10)
            {
                //Debug.Print("Waiting for data (attempt " + failedAttempts + ")...");
                Thread.Sleep(200);
                failedAttempts++;
            }
            if (sock.Available > 0 && sock.Available < 2048) // 2048 for protection against OutOfMemoryException
            {
                // Fetch the data
                byte[] data = new byte[sock.Available];
                sock.Receive(data);
                string text = new string(Encoding.UTF8.GetChars(data));
                data = null;
                if (text.Length < 20) return null; // Most likely malformed
                // Strip away the headers
                foreach (string s in text.Split('\r', '\n'))
                {
                    // Find the line where the format is nn-nn-nn
                    if (s.Length > 10 && isNumeric(s[0]) && isNumeric(s[1]) && isNumeric(s[2]) && isNumeric(s[3]) && s[4] == '-' &&
                        isNumeric(s[5]) && isNumeric(s[6]) && s[7] == '-' &&
                        isNumeric(s[8]) && isNumeric(s[9]))
                        return s;
                }
                return null;
            }
            else
                return null;
        }
        static bool isNumeric(char c)
        {
            return c >= 48 && c <= 57;
        }
    }
    public class Logger
    {
        FileStream fs;
        StreamWriter sw;
        public Logger(string path)
        {
            try
            {
                fs = new FileStream(path, FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write);
                sw = new StreamWriter(fs);
            }
            catch { }
        }
        public void dispose()
        {
            try
            {
                fs.Close();
                fs.Dispose();
            }
            catch { }
        }
        public void write(string desc)
        {
            try
            {
                string s = Program.currentTime.ToString() + " - " + desc;
                Debug.Print(s);
                sw.WriteLine(s);
                sw.Flush();
            }
            catch { }
        }
        public void writeSeparator()
        {
            try
            {
                sw.WriteLine("**************************************************************************************************");
                Debug.Print("**************************************************************************************************");
                sw.Flush();
            }
            catch { }
        }
    }
    public static class Webserver
    {
        #region "Settings"
        public const int port = 81;
        public const int maxClientData = 1024;
        public const int responseChunkSize = 512;
        public const int contentChunkSize = 512;
        public const int allowedIpRange = 10; // 10.x.x.x IP addresses will have special privileges
        public const int concurrentThreads = 1;
        #endregion

        #region "Variables"
        public static DateTime startTime;
        public static Logger log;
        public static bool webserverEnabled = true;
        public static ArrayList requests;
        public static ArrayList threads;
        #endregion

        #region "Methods - Request Handling"
        public static void threadDelegator()
        {
            Thread th;
            Socket client;
            int threadOffset;
            while (true)
            {
                lock (threads)
                {
                    lock (requests)
                    {
                        // Pop any dead threads
                        threadOffset = 0;
                        while (threadOffset < threads.Count)
                        {
                            th = (Thread)threads[threadOffset];
                            if (th.ThreadState == ThreadState.Stopped)
                            {
                                // Ensure the thread has been aborted
                                try { th.Abort(); }
                                catch { }
                                // Pop the thread from the array
                                threads.RemoveAt(threadOffset);
                            }
                            else
                                threadOffset++; // No thread remove, check the next item
                        }
                        // Add new threads
                        if (threads.Count < concurrentThreads && requests.Count > 0)
                        { // Attempt to add every possible request at once
                            for (int i = 0; i < System.Math.Min(concurrentThreads - threads.Count, requests.Count); i++)
                            {
                                client = (Socket)requests[0]; // It will always be zero since we always remove the first item after use
                                requests.RemoveAt(i);
                                th = new Thread(new ThreadStart(
                                    delegate()
                                    {
                                        threadHandler(client);
                                    }));
                                th.Start();
                                threads.Add(th);
                            }
                        }
                        // Dispose resources for this cycle
                        th = null;
                    }
                }
                // Sleep to avoid hogging and to allow new requests to be added...
                Thread.Sleep(50);
            }
        }
        public static void threadHandler(Socket client)
        {
            HttpRequest request = new HttpRequest();
            HttpResponse response = new HttpResponse(((IPEndPoint)client.RemoteEndPoint).Address.ToString());
            try
            {
                // Hnadle the request data
                handleRequest(client, ref request, ref response);
                if (request.requestedPath != null) // Check the request was valid
                {
                    // Delegate the requested
                    request.requestDirs = request.requestedPath.Split('/');
                    log.write("Request from '" + request.clientP.Address + ":" + request.clientP.Port + "', path: '" + (request.requestedPath ?? "not specified") + "' [" + (request.requestDirs.Length > 0 ? "page__" + request.requestDirs[0] : "none") + "] !");
                    if (request.requestedPath.Length == 0)
                        try
                        {
                            page__home(request, response);
                        }
                        catch { }
                    else
                        try
                        {
                            typeof(Webserver).GetMethod("page__" + request.requestDirs[0].ToLower()).Invoke(null, new object[] { request, response });
                        }
                        catch (NullReferenceException)
                        {
                            Debug.Print("404 invoked");
                            page__404(request, response);
                        }
                    // Send the data to the client
                    if (response.contentLength == 0)
                        try { response.finalize(true); } catch { }
                }
                response.buffer.dispose();
            }
            catch (Exception ex)
            {
                log.write("Thread delegator error occurred: " + ex.Message);
                try
                { // Attempt to inform the user
                    client.Send(Encoding.UTF8.GetBytes("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 56\r\nConnection: close\r\n\r\nAn error occurred...prolly ran out of memory lulz ;_;..."));
                }
                catch { }
            }
        }
        /// <summary>
        /// Responsible for launching the web-server and listening for requests.
        /// </summary>
        public static void initServer()
        {
            log = new Logger(Program.logWebserver);
            log.writeSeparator();
            Thread threadDel = null;
            try
            {
                // Create the thread-pool and thread-delegator for handling requests
                requests = new ArrayList();
                threads = new ArrayList();
                threadDel = new Thread(new ThreadStart(threadDelegator));
                threadDel.Start();
                // Create the socket to listen for requests
                Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Bind(new IPEndPoint(IPAddress.Any, port));
                log.write("Binded to '" + Microsoft.SPOT.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()[0].IPAddress + ":" + port + "'!");
                sock.Listen(50);
                Socket client = null;
                startTime = DateTime.Now;
                while (webserverEnabled)
                {
                    try
                    {
                        // Accept the socket
                        client = sock.Accept();
                        client.SendTimeout = 500;
                        requests.Add(client);
                    }
                    catch (Exception ex)
                    {
                        log.write("Error occurred: " + ex.Message);
                        try
                        { // Attempt to inform the user
                            client.Send(Encoding.UTF8.GetBytes("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 56\r\nConnection: close\r\n\r\nAn error occurred...prolly ran out of memory lulz ;_;..."));
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                log.write("Webserver failed (" + ex.Message + "), reinitializing....");
                initServer();
            }
            finally
            {
                threadDel.Abort();
            }
            log.dispose();
        }
        /// <summary>
        /// Responsible for parsing the request, ready for delegation.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="request"></param>
        /// <param name="response"></param>
        public static void handleRequest(Socket client, ref HttpRequest request, ref HttpResponse response)
        { // We use this as a separate method so every variable inside of here will be free for garbage collection - more efficient
            response.client = client;
            request.clientP = client.RemoteEndPoint as IPEndPoint;
            StringBuilder data = new StringBuilder();
            byte[] rawData;
            // Recieve the request data
            rawData = new byte[maxClientData];
            client.Receive(rawData, rawData.Length, SocketFlags.None); // Recieve the data and append to buffer
            data.Append(Encoding.UTF8.GetChars(rawData));
            // Handle the request
            if (data.Length > 0)
            {
                // Remove \r chars
                data.Replace("\r", ""); // Strip /r chars - useless and annoying
                Debug.Print(data.ToString());
                // Reset requested path
                bool postData = false;
                bool blankLine = false;
                int keySeparator;
                foreach (string line in data.ToString().Split('\n'))
                {
                    if (line.Length > 10 && line.Substring(0, 4) == "GET " && line.Substring(line.Length - 9, 9) == " HTTP/1.1")
                    {
                        request.requestedPath = line.Substring(4);
                        request.requestedPath = request.requestedPath.Substring(1, request.requestedPath.Length - 10);
                    }
                    else if (line.Length > 10 && line.Substring(0, 5) == "POST " && line.Substring(line.Length - 9, 9) == " HTTP/1.1")
                    {
                        request.requestedPath = line.Substring(5);
                        request.requestedPath = request.requestedPath.Substring(1, request.requestedPath.Length - 10);
                        postData = true;
                    }
                    else if (line.Length > 13 && line.Substring(0, 12) == "User-Agent: ")
                        request.userAgent = line.Substring(12);
                    else if (line.Length == 0)
                    {
                        if (postData)
                            blankLine = true;
                        else
                            break;
                    }
                    else if (blankLine && line.Length > 4)
                        foreach (string subline in line.Split('&'))
                            if (subline.Length >= 3 && (keySeparator = subline.IndexOf('=')) != -1 && keySeparator < subline.Length - 1)
                                request[subline.Substring(0, keySeparator)] = subline.Substring(keySeparator + 1);
                }
                data = null;
                rawData = null;
            }
            else
                client.Close();
        }
        #endregion

        #region "Methods - Pages"
        public static void page__home(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            writePage_Header(request, response, "Homepage");
            response.buffer.Append(
@"
<p>Welcome to Evilduino's homepage!</p>
<p>
    My name is Evilduino, an evil <a href=""http://netduino.com/"">Netduino</a> robot running off the local power-grid with a range of morally
    questionable and useful tools; you can also find my source-code, for super-fun hacking time, available for free on Github
    <a href=""").Append(Program.github).Append(@""">here</a> - feel free to contribute or build your own Evilduino army!
</p>
<p>
    <img class=""FL"" src=""http://earthobservatory.nasa.gov/Features/vonBraun/Images/apollo_11_launch.jpg"" alt=""Apollo 11 launch"" />
    Fun-fact: the Netduino has only 64kb of RAM available - this includes the available RAM for both your requests to this site and the webserver+binary-clock software! This is <b>equivalent</b> to the memory available on the spacecraft which landed on the moon in 1969 (<a href=""http://www.itpro.co.uk/612913/man-on-the-moon-technology-then-and-now"">source</a>).
</p>
<p>
    ...so if the Evilduino only has 64kb (0.0625% of a megabyte or 0.000061% of a gigabyte) of RAM, how are you viewing the above banner...which is 112kb? You'd be surprised how little memory is required to provide such services.
</p>
");
            writePage_Footer(request, response);
        }
        public static void page__fapgame(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            writePage_Header(request, response, "Random Fap Game");
            string[] fapSite = new string[] { "Free choice", "Motherless", "Redtube", "Facebook - class-mate", "Facebook - friend of the same sex", "Facebook - friend of the opposite sex", "Facebook - you choose", "4chan /b/", "4chan /s/" };
            string[] fapForeplay = new string[] { "Free choice", "None", "Use a vibrator", "Use a dildo", "Use a finger", "Hump a pillow", "Hump a toy or object", "Thrust a piece of food", "Posh wank - use a condom", "Fap in your pants", "Use 3 fingers", "Fap with relatives under-wear", "Smear lube all over your body", "Smear cream all over your body", "In the shower - use shampoo - need sparkly pubes" };
            string[] fapEnding = new string[] { "Free choice", "Spread across body", "Record yourself eating the cum from your hand", "Clean-up and re-roll for a second fap", "Fap into your mouth - or eat it", "Choose yourself", "Boring - jizz into a tissue", "Fap into sock - saves having to clean it up" };
            string[] fapSharing = new string[] { "Free choice", "Nothing, take le day off.", "Post on 4chan : /b/!", "Send to a friend on Facebook!", "Omegle", "Chatroulette" };
            Random rand = new Random((Program.currentTime.Millisecond * Program.currentTime.Second * Program.currentTime.Hour) - (int)(Program.light * Program.temperature));
            response.buffer
                .Append("<h2>Fap Site</h2><p>").Append(fapSite[rand.Next(fapSite.Length - 1)]).Append("</p>")
                .Append("<h2>Foreplay</h2><p>").Append(fapForeplay[rand.Next(fapForeplay.Length - 1)]).Append("</p>")
                .Append("<h2>Ending</h2><p>").Append(fapEnding[rand.Next(fapEnding.Length - 1)]).Append("</p>")
                .Append("<h2>Sharing</h2><p>").Append(fapSharing[rand.Next(fapSharing.Length - 1)]).Append("</p>");
            response.cacheControl = null;
            writePage_Footer(request, response);
        }
        public static void page__redtube(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            Random rand = new Random();
            response.statusCode = HttpResponse.StatusCode.REDIRECTION__Temporary_Redirect;
            response.cacheControl = null;
            response.additionalHeaders.Add("Location", "http://www.redtube.com/" + (1 + rand.Next(100000)));
        }
        public static void page__reboot(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            if (request.clientP.Address.GetAddressBytes()[0] != allowedIpRange)
            {
                writePage_Header(request, response, "Reboot Evilduino");
                response.buffer.Append(
@"
<p>Access denied! Only the gateway may carry-out this operation, <a href=""../home"">home</a>?</p>
");
                writePage_Footer(request, response);
            }
            else
                webserverEnabled = false;
        }
        public static void page__404(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            writePage_Header(request, response, "404 - Resource Not Found ;_;");
            response.buffer.Append(
@"
<p>The requested resource could not be found!</p>
<img src=""http://captionsearch.com/pix/thumb/54dhkrj63g-t.jpg"" alt=""Lolcats 404"" />
<p>...learn to internets, Evilduino doesn't make mistakes...</p>
");
            writePage_Footer(request, response);
        }
        public static void page__content(HttpRequest request, HttpResponse response)
        {
            request.disposeFormData();
            if (request.requestDirs.Length < 1) page__404(request, response);
            else if (File.Exists("\\SD\\Content\\" + request.requestDirs[1]))
            {
                FileStream fs = new FileStream("\\SD\\Content\\" + request.requestDirs[1], FileMode.Open, FileAccess.Read);
                // Set the content type
                string extension = Path.GetExtension(request.requestDirs[1]);
                Debug.Print(extension);
                switch (extension)
                {
                    case ".css":
                        response.contentType = "text/css"; break;
                    case ".png":
                        response.contentType = "image/png"; break;
                    case ".jpeg":
                    case ".jpg":
                        response.contentType = "image/jpg"; break;
                    case ".gif":
                        response.contentType = "image/gif"; break;
                    default:
                        response.contentType = "application/data"; break;
                }
                extension = null;
                // To get around RAM issues, we'll end the response here by specifying the length and sending chunks
                response.contentLength = (int)fs.Length;
                response.finalize(false);
                try
                {
                    
                    byte[] data;
                    for (int bytesRead = 0; bytesRead < fs.Length; bytesRead += contentChunkSize)
                    {
                        data = new byte[contentChunkSize];
                        fs.Read(data, 0, contentChunkSize);
                        response.client.Send(data);
                    }
                    data = null;
                }
                catch { }
                finally
                {
                    fs.Close();
                    response.client.Close();
                }
            }
            else page__404(request, response);
        }
        static object guestbookIOLocking = new object();
        public static void page__evilduino(HttpRequest request, HttpResponse response)
        {
            switch (request.requestDirs.Length >= 2 ? request.requestDirs[1] : null)
            {
                case "about":
                    request.disposeFormData();
                    writePage_Header(request, response, "Evilduino - About");
                    response.buffer.Append(
@"
<h2>Features</h2>
<ul>
    <li>Binary clock LED matrix.</li>
    <li>Sensors - light, temperature and tilt.</li>
    <li>Webserver - which you're using right now.</li>
    <ul>
        <li>Ability to handle large amounts of data, despite limited capacity.</li>
        <li>Supports GET and POST requests with form data.</li>
        <li>Background changes relative to the amount of light.</li>
        <li>Large file uploading.</li>
        <li>Paged buffer to get around RAM issues whilst building the HTML for pages (our secret ingredient).</li>
        <li>Supports URL encoding.</li>
    </ul>
    <li>Buzzer and alarm system.</li>
    <li>Internet-controlled time.</li>
    <li>Multi-threaded - with about seven threads running simultaneously!</li>
</ul>
<h2>Specs</h2>
<ul>
    <li>64 kilobytes of RAM - only 28 kilobytes for runtime data!</li>
    <li>One-gigabyte of storage.</li>
    <li>48 MHz ARM7 Atmel 32-bit processor - MIPS (RISC) architecture.</li>
    <li>100 mbps NIC.</li>
    <li>7.5-12.0 VDC USB power - 200 mmA max.</li>
</ul>
<h2>YouTube</h2>
<iframe width=""420"" height=""315"" src=""http://www.youtube.com/embed/q6_V7x5XT5U"" frameborder=""0"" allowfullscreen></iframe>

<h2>Images</h2>
<img src=""/Content/Evilduino1.jpg"" alt=""Evilduino"" class=""THUMB"" />
<img src=""/Content/Evilduino2.jpg"" alt=""Evilduino"" class=""THUMB"" />
<img src=""/Content/Evilduino3.jpg"" alt=""Evilduino"" class=""THUMB"" />
");
                    break;
                case "guestbook":
                    writePage_Header(request, response, "Evilduino - Guestbook");
                    switch (request.requestDirs.Length >= 3 ? request.requestDirs[2] : null)
                    {
                        default:
                            const int postsPerPage = 5;
                            int page = request.requestDirs.Length >= 3 ? tryParse(request.requestDirs[2], 1) : 1;
                            response.buffer.Append("<h2>Posts - Page " + page + "</h2>");
                            lock (guestbookIOLocking)
                            {
                                FileStream f = new FileStream("\\SD\\Guestbook.txt", FileMode.OpenOrCreate, FileAccess.Read);
                                StreamReader sr = new StreamReader(f);
                                // Read into the posts
                                for (int i = 0; i < (postsPerPage * page) - postsPerPage; i++)
                                    sr.ReadLine();
                                // Display posts
                                int separator;
                                string line;
                                for (int i = 0; i < postsPerPage; i++)
                                {
                                    line = sr.ReadLine();
                                    if (line != null && line.Length > 0)
                                    {
                                        separator = line.IndexOf('¬');
                                        if(separator != -1)
                                            response.buffer.Append(
        @"
    <div class=""GB_MSG"">
    <p>
    ").Append(line.Substring(separator + 1)).Append(@"
    </p>
    ").Append(line.Substring(0, separator)).Append(@" <a href=""/evilduino/guestbook/delete/").Append(((page * postsPerPage) - postsPerPage) + i).Append(@""">[Delete]</a>").Append(@"
    </div>
    ");
                                    }
                                }
                                sr.Close();
                                sr.Dispose();
                                f.Close();
                                f.Dispose();
                            }
                            response.buffer.Append(
@"
<p>
    <a href=""/evilduino/guestbook/post"">Make a Post</a> Pages: <a href=""/evilduino/guestbook/").Append(page > 1 ? page - 1 : 1).Append(@""">Previous</a> <a href=""").Append(page == int.MaxValue ? int.MaxValue : page + 1).Append(@""">Next</a>
</p>
");
                            break;
                        case "post":
                            if (request["message"] != null && request["message"].Length > 0)
                            {
                                // Write the post to the guestbook file
                                StringBuilder data = new StringBuilder(urlDecode(request["message"], true));
                                // Write to file
                                lock (guestbookIOLocking)
                                {
                                    FileStream f = new FileStream("\\SD\\Guestbook.txt", FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write);
                                    StreamWriter sw = new StreamWriter(f);
                                    sw.WriteLine(Program.currentTime + "¬" + data);
                                    sw.Flush();
                                    sw.Close();
                                    sw.Dispose();
                                    f.Close();
                                    f.Dispose();
                                }
                                // Redirect
                                response.additionalHeaders["Location"] = "/evilduino/guestbook";
                                response.statusCode = HttpResponse.StatusCode.REDIRECTION__Temporary_Redirect;
                            }
                            else
                            {
                                // Display the form
                                response.buffer.Append(
@"
<h2>Make a Post</h2>
<form method=""post"" action=""/evilduino/guestbook/post"">
    <div>
        <p>
            Your message:
            <textarea name=""message""></textarea>
        </p>
        <p>
            <input type=""submit"" value=""Post"" />
        </p>
    </div>
</form>
");
                            }
                            break;
                        case "delete":
                            break;
                    }
                    break;
                case "visitors":
                    request.disposeFormData();
                    break;
            }
            writePage_Footer(request, response);
        }
        public static void page__settings(HttpRequest request, HttpResponse response)
        {
            response.cacheControl = null;
            writePage_Header(request, response, "Settings");
            switch (request.requestDirs.Length >= 2 ? request.requestDirs[1] : null)
            {
                default:
                    response.buffer.Append(
@"<h2>Menu</h2>
<p><a href=""/settings/alarm"">Set Alarm</a></p>
<p><a href=""/settings/light"">Set Light</a></p>
");
                    break;
                case "alarm":
                    int day = tryParse(request["day"], -1);
                    int month = tryParse(request["month"], -1);
                    int year = tryParse(request["year"], -1);
                    int hour = tryParse(request["hour"], -1);
                    int min = tryParse(request["minute"], -1);
                    int sec = tryParse(request["second"], -1);
                    if (day > -1 && month > -1 && year > -1 && hour > -1 && min > -1 && sec > -1)
                    {
                        if (new DateTime(year, month, day, hour, min, sec).Subtract(Program.currentTime).Seconds < 1)
                            response.buffer.Append(
@"<h2>Set Alarm - Failure</h2>
<p>The specified time would immediately trigger the alarm - pick a future time!</p>
<p><a href=""/settings"">Settings</a><p>
<p><a href=""/settings/alarm"">Settings - alarm</a><p>
<p><a href=""/home"">Home</a><p>
");
                        else
                        {
                            Program.alarm = new DateTime(year, month, day, hour, min, sec);
                            response.buffer.Append(
    @"<h2>Set Alarm - Updated alarm successfully</h2>
<p>Updated successfully to '" + Program.alarm.ToString("dd/MM/yyyy HH:mm:ss") + @"'...</p>
<p><a href=""/settings"">Settings</a><p>
<p><a href=""/settings/alarm"">Settings - alarm</a><p>
<p><a href=""/home"">Home</a><p>
");
                        }
                    }
                    else
                    {
                        DateTime tomorrow = Program.alarm != DateTime.MaxValue ? Program.alarm : Program.currentTime.AddDays(1);
                        response.buffer.Append(
@"<h2>Set Alarm</h2>
<form method=""post"" action=""/settings/alarm"">
<div>
    <p>Year: <input type=""text"" name=""year"" value=""").Append(year > -1 ? year : tomorrow.Year).Append(@""" /></p>
    <p>Month: <input type=""text"" name=""month"" value=""").Append(month > -1 ? month : tomorrow.Month).Append(@""" /></p>
    <p>Day: <input type=""text"" name=""day"" value=""").Append(day > -1 ? day : tomorrow.Day).Append(@""" /></p>
    <p>Hour: <input type=""text"" name=""hour"" value=""").Append(hour > -1 ? hour : tomorrow.Hour).Append(@""" /></p>
    <p>Minute: <input type=""text"" name=""minute"" value=""").Append(min > -1 ? min : tomorrow.Minute).Append(@""" /></p>
    <p>Second: <input type=""text"" name=""second"" value=""").Append(sec > -1 ? sec : tomorrow.Second).Append(@""" /></p>
    <p><input type=""submit"" value=""Set"" />
</div>
<div>Current alarm: ").Append(Program.alarm != DateTime.MaxValue ? Program.alarm.ToString("dd/MM/yyyy HH:mm:ss") : "none").Append(@"</div>
</form>
");
                    }
                    break;
                case "light":
                    break;
            }
            writePage_Footer(request, response);
        }
        public static void page__files(HttpRequest request, HttpResponse response)
        {
            const string title = "File Upload";
            // Check the user is allowed access
            if (request.clientP.Address.GetAddressBytes()[0] != allowedIpRange)
            {
                writePage_Header(request, response, title);
                response.buffer.Append(
@"<h2>Access Denied</h2>
<img src=""http://th127.photobucket.com/albums/p123/FFantasygrl/th_webcamedit.jpg"" alt=""Srsfase"" class=""FR"" />
<p>You are not on the local network, gtfo... <a href=""/home"">home?</a></p>");
                writePage_Footer(request, response);
                return;
            }
            // Handle the request
            switch(request.requestDirs.Length >= 2 ? request.requestDirs[1] : null)
            {
                default:
                    writePage_Header(request, response, title);
                    response.buffer.Append(
@"
<p>Use the following section of the site to manage the files on the Evilduino.</p>
<p><a href=""/files/folder"">File Browser</a></p>
<p><a href=""/files/upload"">Upload a File</a></p>
");
                    writePage_Footer(request, response);
                    break;
                case "delete":          // Delete a file
                    break;
                case "folder":          // View a folder
                    break;
                case "upload":          // Upload form
                    request.disposeFormData();
                    writePage_Header(request, response, title);
                    response.buffer.Append(
@"<h2>Files - Upload a File</h2>
<p>This will replace an existing file!</p>
<form method=""post"" action=""/files/upload_handle"">
    <div class=""FILE"">
        <div>File-name:</div>
        <div><input type=""text"" name=""filename"" /></div>
    </div>
    <div class=""FILE"">
        <div>Data:</div>
        <div><textarea name=""data"" cols=""60"" rows=""20""></textarea></div>
    </div>
    <input type=""submit"" value=""Upload"" />
</form>");
                    writePage_Footer(request, response);
                    break;
                case "upload_handle":   // Upload a file from the user
                    string error = null; // Used to inform the user of an error
                    string filename = request["filename"]; // The data will be written to a temp file if no file field was found
                    // Check the cache directory exists
                    if (!Directory.Exists("\\SD\\Cache")) Directory.CreateDirectory("\\SD\\Cache");
                    // Generate the temp name
                    string tempFilename = "\\SD\\Cache\\" + Program.currentTime.ToString("yyyy_MM_dd_HH_mm_ss") + ".tmp";
                    // Open the file for writing
                    FileStream fs = new FileStream(tempFilename, FileMode.Create, FileAccess.Write);
                    StreamWriter sw = new StreamWriter(fs);
                    // Begin writing data to the file
                    byte[] chunk;
                    char[] sChunk;
                    // - Check if we've already picked up some of the data
                    if(request["data"] != null)
                    {
                        sChunk = request["data"].ToCharArray();
                        sw.Write(sChunk);
                    }
                    // - Begin reading other available, possibly overflowed, field data
                    int iterations = 0;
                    const int chunkSize = 256;
                    bool fileNameOverflow = false;
                    bool dataOverflow = false;
                    char[] urlEncodeOverflow = null;
                    while (iterations < 20)
                    {
                        chunk = new byte[chunkSize];
                        // Recieve data until there is no more available
                        if (response.client.Available > 0 && response.client.Receive(chunk, chunk.Length, SocketFlags.Partial) > 0)
                        {
                            sChunk = Encoding.UTF8.GetChars(chunk);
                            handleUploadData(ref fileNameOverflow, ref dataOverflow, ref sChunk, ref sw, ref filename, ref urlEncodeOverflow);
                        }
                        else
                            break;
                        iterations++;
                    }
                    // Close the file
                    sw.Flush();
                    sw.Close();
                    sw.Dispose();
                    fs.Close();
                    fs.Dispose();
                    // Rename the file
                    try
                    {
                        if (File.Exists("\\SD\\Content\\" + filename))
                            File.Delete("\\SD\\Content\\" + filename);
                        File.Move(tempFilename, "\\SD\\Content\\" + filename);
                    }
                    catch(Exception ex) { error = "Failed to rename file: " + ex.Message + "!"; }
                    // Check if the loop exited due to a large file
                    if(iterations == 20 && response.client.Available > 0) error = "File too large!";
                    // Free memory
                    request.disposeFormData();
                    chunk = null;
                    sChunk = null;
                    // Finalize request
                    writePage_Header(request, response, title);
                    if(error != null)
                    {
                        try { File.Delete(tempFilename); } catch {}
                        try { File.Delete("\\SD\\Content\\" + filename); }
                        catch { }
                        // Error occurred - delete the file and inform the user
                        response.buffer.Append(@"<h2>Files - Failed to Upload</h2><p>Failed to upload file: </p><p>").Append(error).Append(@"</p><p><a href=""/files/upload"">Try again?</a>");
                    }
                    else
                        response.buffer.Append(
@"<h2>Files - Uploaded Successfully</h2>
<p>File <a href=""/Content/").Append(filename).Append(@""">'").Append(filename).Append(@"'</a> successfully uploaded!</p>
<p><a href=""/files/upload"">Upload another file</a></p>
<p><a href=""/files"">Files - Home</a></p>
<p><a href=""/home"">Home</a></p>
");
                    writePage_Footer(request, response);
                    break;
            }
        }
        #endregion

        #region "Methods - Site Template"
        /// <summary>
        /// Appends the header of the global template.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <param name="title"></param>
        public static void writePage_Header(HttpRequest request, HttpResponse response, string title)
        {
            string background = ((int)(Program.light / 100 * 255)).ToString("X");
            response.buffer.Append(
@"
<!DOCTYPE html PUBLIC ""-//W3C//DTD XHTML 1.0 Strict//EN"" ""http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd"">
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head>
<title>Evilduino - ").Append(title).Append(@"</title>
<link rel=""Stylesheet"" type=""text/css"" href=""/Content/Style.css"" />
</head>
<body style=""background: #").Append(background).Append(background).Append(background).Append(@""">
<div class=""WRAPPER"">
<div class=""BANNER"">
    <div class=""STATS_PANEL"">
        <div><b>System time:</b> ").Append(Program.currentTime).Append(@"</div>
        <div><b>Temperature:</b> ").Append(Program.temperature).Append(@"°C</div>
        <div><b>Light:</b> ").Append(Program.light).Append(@"%</div>
    </div>
	<img src=""/Content/Banner.png"" alt=""Evilduino Logo"" />
	<div class=""clear""></div>
</div>
<div class=""NAV"">
    <a href=""/home"">Home</a>
	<div>Runtime Management:</div>
	<a href=""/settings"">Settings</a>
    <a href=""/files"">Files</a>		

	<div>Computing Tools</div>
	<a href=""/computing/bases"">Convert Bases</a>
	<a href=""/computing/matrices"">Matrix Multiplier</a>
    <a href=""/computing/euler"">Project Euler</a>
    <a href=""/computing/euler"">Solve 3D Linear System</a>
		
	<div>Evilduino</div>
    <a href=""/evilduino/about"">About</a>
    <a href=""/evilduino/guestbook"">Guestbook</a>
		
	<div>Porn Tools</div>
	<a href=""/redtube"">Random Redtube Vidya</a>
	<a href=""http://motherless.com/random/video"">Random Motherless Vidya</a>
	<a href=""/fapgame"">Random Fap Game</a>
</div>
<div class=""CONTENT"">
        <h1>").Append(title).Append(@"</h1>
");
        }
        /// <summary>
        /// Appends the footer of the global template.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        public static void writePage_Footer(HttpRequest request, HttpResponse response)
        {
            response.buffer.Append(
@"
</div>
<div class=""FOOTER"">
	<b>Uptime:</b> ").Append(DateTime.Now.Subtract(startTime).ToString()).Append(@"
	<b>Version:</b>1.0.0.evil
	<a href=""http://www.ubermeat.co.uk/"">Ubermeat</a>
	<a href=""").Append(Program.github).Append(@""">Github</a>
</div>
<div class=""clear""></div>
</div>
<div class=""FOOTER_NOTES"">
<p>Creative Commons Attribution-ShareAlike 3.0 unported</p>
</div>
</body>
</html>
");
        }
        #endregion

        #region "Methods"
        /// <summary>
        /// Attempts to parse a string as an integer, else it returns the specified failValue substitute.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="failValue"></param>
        /// <returns></returns>
        static int tryParse(string value, int failValue)
        {
            if (value == null) return failValue;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < 48 || value[i] > 57)
                    return failValue;
            return int.Parse(value);
        }
        /// <summary>
        /// Decodes a URL-encoded string.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="htmlProtection"></param>
        /// <returns></returns>
        static string urlDecode(string value, bool htmlProtection)
        {
            StringBuilder sb = new StringBuilder(value);
            sb.Replace("+", " ");
            value = null;
            int index = 0;
            while (true)
            {
                if (index >= sb.Length) break;
                if (sb[index] == '%' && sb.Length - index > 2 && (((sb[index + 1] >= 48 && sb[index + 1] <= 57) || (sb[index + 1] >= 65 && sb[index + 1] <= 70)) && ((sb[index + 2] >= 48 && sb[index + 2] <= 57) || (sb[index + 2] >= 65 && sb[index + 2] <= 70))))
                {
                    // Get the hex char and replace the 3 chars with it
                    sb[index] = convertHexToChar(new char[] { sb[index + 1], sb[index + 2] });
                    sb.Remove(index + 1, 2);
                }
                index++;
            }
            if (htmlProtection)
            {
                sb.Replace("<", "&lt;");
                sb.Replace(">", "&gt;");
                sb.Replace("\r", "");
                sb.Replace("\n", "<br />");
            }
            return sb.ToString();
        }
        /// <summary>
        /// Handles the data of a POST request for the method page__upload; this currently extracts the values for the
        /// fields "filename" and "data".
        /// </summary>
        /// <param name="fileNameOverflow"></param>
        /// <param name="dataOverflow"></param>
        /// <param name="sChunk"></param>
        /// <param name="sw"></param>
        /// <param name="filename"></param>
        /// <param name="urlEncodeOverflow"></param>
        static void handleUploadData(ref bool fileNameOverflow, ref bool dataOverflow, ref char[] sChunk, ref StreamWriter sw, ref string filename, ref char[] urlEncodeOverflow)
        {
            if (fileNameOverflow)
            {
                string newStr = substring(ref sChunk);
                filename += newStr;
                if (filename.Length != sChunk.Length)
                {
                    // +1 because we want to ignore possible &'s
                    sChunk = substring(ref sChunk, newStr.Length + 1).ToCharArray();
                    handleUploadData(ref fileNameOverflow, ref dataOverflow, ref sChunk, ref sw, ref filename, ref urlEncodeOverflow);
                }
            }
            else if (dataOverflow)
            {
                int separatorIndex = indexOf(ref sChunk, '&');
                sw.Write(urlDecodeWithOverflow(substring(ref sChunk, 0, separatorIndex != -1 ? separatorIndex : sChunk.Length).ToCharArray(), ref urlEncodeOverflow));
                if (separatorIndex != -1)
                {
                    sChunk = substring(ref sChunk, separatorIndex).ToCharArray();
                    handleUploadData(ref fileNameOverflow, ref dataOverflow, ref sChunk, ref sw, ref filename, ref urlEncodeOverflow);
                }
            }
            else
            {
                int separatorIndex = indexOf(ref sChunk, '=');
                string fieldName = substring(ref sChunk, 0, separatorIndex);
                if (fieldName == "filename")
                {
                    filename = substring(ref sChunk, separatorIndex + 1);
                    // Check if we got the entire filename, else we'll throw an overflow
                    if (filename.Length != sChunk.Length)
                    {
                        // +1 because we want to ignore possible &'s
                        sChunk = substring(ref sChunk, separatorIndex + filename.Length + 2).ToCharArray();
                        handleUploadData(ref fileNameOverflow, ref dataOverflow, ref sChunk, ref sw, ref filename, ref urlEncodeOverflow);
                    }
                    else
                        // We may be expecting more data
                        fileNameOverflow = true;
                }
                else if (fieldName == "data")
                {
                    string data = substring(ref sChunk, separatorIndex + 1);
                    sw.Write(urlDecodeWithOverflow(data.ToCharArray(), ref urlEncodeOverflow));
                    if (indexOf(ref sChunk, '&', separatorIndex + data.Length + 2) != -1)
                    {
                        sChunk = substring(ref sChunk, data.Length).ToCharArray();
                        handleUploadData(ref fileNameOverflow, ref dataOverflow, ref sChunk, ref sw, ref filename, ref urlEncodeOverflow);
                    }
                    else
                        // We may be expeting more data
                        dataOverflow = true;
                }
            }
        }
        /// <summary>
        /// Decodes URL-encoded text with the ability to detect cut-off encoded chars, which are placed into an overflow array;
        /// this array can then be used when the method is invoked again.
        /// </summary>
        /// <param name="chars"></param>
        /// <param name="urlEncodeOverflow"></param>
        /// <returns></returns>
        static char[] urlDecodeWithOverflow(char[] chars, ref char[] urlEncodeOverflow)
        {
            StringBuilder sb = new StringBuilder((urlEncodeOverflow != null && urlEncodeOverflow.Length > 0 ? new string(urlEncodeOverflow) : string.Empty) + new string(chars));
            urlEncodeOverflow = null;
            sb.Replace("+", " ");
            int index = 0;
            while (true)
            {
                if (index >= sb.Length) break;
                if (sb[index] == '%' && (sb.Length - index <= 2 || (((sb[index + 1] >= 48 && sb[index + 1] <= 57) || (sb[index + 1] >= 65 && sb[index + 1] <= 70)) && ((sb[index + 2] >= 48 && sb[index + 2] <= 57) || (sb[index + 2] >= 65 && sb[index + 2] <= 70)))))
                {
                    if (sb.Length - index > 2)
                    {
                        sb[index] = convertHexToChar(new char[] { sb[index + 1], sb[index + 2] });
                        sb.Remove(index + 1, 2);
                    }
                    else if (sb.Length - index > 1)
                    {
                        urlEncodeOverflow = new char[] { '%', sb[index + 1] };
                        sb.Remove(index, 2);
                    }
                    else
                    {
                        urlEncodeOverflow = new char[] { '%' };
                        sb.Remove(index, 1);
                    }
                }
                index++;
            }
            return sb.ToString().ToCharArray();
        }
        /// <summary>
        /// Converts a char-array of hex-values to an integer; for instance
        /// you might pass new char[]{ '0', 'A' }, which would become 10.
        /// </summary>
        /// <param name="chars"></param>
        /// <returns></returns>
        static char convertHexToChar(char[] chars)
        {
            return (char)Convert.ToInt32(new string(chars), 16);
        }
        /// <summary>
        /// Returns the index, starting at zero, of the first occurrence of a char found - when going left to right.
        /// </summary>
        /// <param name="chars"></param>
        /// <param name="charr"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        static int indexOf(ref char[] chars, char charr, int startIndex = 0)
        {
            for (int i = startIndex; i < chars.Length; i++)
                if (chars[i] == charr) return i;
            return -1;
        }
        /// <summary>
        /// Returns the string for a specified range of a char-array, with the ability to terminate the string
        /// before a specified abortChar.
        /// </summary>
        /// <param name="chars"></param>
        /// <param name="index"></param>
        /// <param name="length"></param>
        /// <param name="abortChar"></param>
        /// <returns></returns>
        static string substring(ref char[] chars, int index = 0, int length = 0, char abortChar = '&')
        {
            StringBuilder sb = new StringBuilder();
            for (int i = index; i < (length == 0 ? chars.Length : length); i++)
                if (chars[i] == abortChar)
                    break;
                else
                    sb.Append(chars[i]);
            return sb.ToString();
        }
        #endregion
    }
    /// <summary>
    /// A data-structure to represent information of a HTTP request.
    /// </summary>
    public class HttpRequest
    {
        /// <summary>
        /// The IP and port of the client.
        /// </summary>
        public IPEndPoint clientP = null;
        /// <summary>
        /// The requested path of the client.
        /// </summary>
        public string requestedPath = null;
        /// <summary>
        /// The requested path broken-up by directories/tailing-slash.
        /// </summary>
        public string[] requestDirs = null;
        /// <summary>
        /// The browser/user-agent of the client.
        /// </summary>
        public string userAgent = null;
        /// <summary>
        /// Form data sent by the client.
        /// </summary>
        public Hashtable formData = null;

        /// <summary>
        /// Form data.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string this[string key]
        {
            get
            {
                if (formData == null) return null;
                else if(formData.Contains(key)) return (string)formData[key];
                else return null;
            }
            set
            {
                if (formData == null) formData = new Hashtable();
                formData[key] = value;
                lastKey = key;
            }
        }
        /// <summary>
        /// The last POST field key-name inserted into the formData hashtable.
        /// </summary>
        public string lastKey = null;
        /// <summary>
        /// Useful for methods to clear unused memory.
        /// </summary>
        public void disposeFormData()
        {
            if (formData != null)
            {
                lock (formData) formData.Clear();
                formData = null;
                lastKey = null;
            }
        }
    }
    /// <summary>
    /// A data-structure to handle the response of a HTTP request.
    /// </summary>
    public class HttpResponse
    {
        #region "Enums"
        public enum StatusCode
        {
            SUCCESS__OK = 200,
            REDIRECTION__Temporary_Redirect = 307,
            CLIENT_ERROR__Forbidden = 403,
            CLIENT_ERROR__Not_Found = 404,
            SERVER_ERROR__Internal_Server_Error = 500
        }
        #endregion

        #region "Variables"
        /// <summary>
        /// The socket used for communicating with the client.
        /// </summary>
        public Socket client;
        /// <summary>
        /// The status code of the response.
        /// </summary>
        public StatusCode statusCode = StatusCode.SUCCESS__OK;
        /// <summary>
        /// The MIME response type of the response-buffer.
        /// </summary>
        public string contentType = "text/html";
        /// <summary>
        /// The enforced behaviour for the client's cache when handling this entity. Refer to:
        /// http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html - 14.9 Cache-Control
        /// 
        /// Typical values:
        /// no-cache
        /// no-store
        /// max-age=delta-seconds
        /// 
        /// You can also set this value as null to disable the field being sent in the headers.
        /// </summary>
        public string cacheControl = "max-age=3600";
        /// <summary>
        /// The buffer used for sending string-data to the client; will not be used if bufferBytes is defined.
        /// </summary>
        public PagedStringBuilder buffer;
        /// <summary>
        /// Additional headers to be sent in the HTTP header.
        /// </summary>
        public Hashtable additionalHeaders = new Hashtable();
        /// <summary>
        /// The length of the content; this is only used if sendContent for finalize is set to false.
        /// </summary>
        public int contentLength = 0;
        #endregion

        #region "Methods - Constructors"
        public HttpResponse(string ip)
        {
            this.buffer = new PagedStringBuilder(ip);
        }
        #endregion

        #region "Methods"
        /// <summary>
        /// Finalizes the response by sending the headers and response buffer and terminating the transcation.
        /// </summary>
        public void finalize(bool sendContent)
        {
            // Build the status-code
            string responseCode;
            switch (statusCode)
            {
                case StatusCode.CLIENT_ERROR__Forbidden:
                    responseCode = "403 Forbidden"; break;
                case StatusCode.CLIENT_ERROR__Not_Found:
                    responseCode = "404 Not Found"; break;
                case StatusCode.REDIRECTION__Temporary_Redirect:
                    responseCode = "307 Temporary Redirect"; break;
                case StatusCode.SUCCESS__OK:
                    responseCode = "200 OK"; break;
                case StatusCode.SERVER_ERROR__Internal_Server_Error:
                default:
                    responseCode = "500 Internal Server Error"; break;
            }
            // Build the headers
            StringBuilder headerB = new StringBuilder();
            headerB
                .Append("HTTP/1.1 ").Append(responseCode).Append("\r\n")
                .Append("Server: Evilduino\r\n")
                .Append("Content-Type: ").Append(contentType).Append("; charset=utf-8\r\n");
            if (cacheControl != null)
                headerB.Append("Cache-Control: " + cacheControl + "\r\n");
            headerB
                .Append("Content-Length: ").Append(!sendContent ? contentLength : buffer.Length).Append("\r\n");
            // -- Append additional headers
            foreach (DictionaryEntry h in additionalHeaders)
                headerB.Append((string)h.Key).Append(": ").Append((string)h.Value).Append("\r\n");
            headerB.Append("Connection: close\r\n\r\n");
            // Build the header bytes
            byte[] header = Encoding.UTF8.GetBytes(headerB.ToString());
            headerB.Clear(); // Free the memory
            headerB = null;
            responseCode = null;
            // Send the bytes to the client
            client.Send(header);
            header = null;
            if (sendContent)
            {
                char[] chars = new char[Webserver.responseChunkSize];
                int length;
                byte[] chunk;
                buffer.beginRead();
                for (int i = 0; i < buffer.Length; i += Webserver.responseChunkSize)
                {
                    length = buffer.Length - (i + Webserver.responseChunkSize) > 0 ? Webserver.responseChunkSize : buffer.Length - i;
                    chunk = new byte[length];
                    buffer.read(chunk, 0, chunk.Length);
                    client.Send(chunk);
                }
                client.Close();
                chars = null;
                chunk = null;
            }
        }
        #endregion
    }
    /// <summary>
    /// A replacement of StringBuilder with similar functionality, except it uses paged memory (opposed to RAM) for buffer storage.
    /// </summary>
    public class PagedStringBuilder
    {
        string path;
        FileStream fs;
        StreamWriter sw;
        /// <summary>
        /// Creates a new instance of a paged-memory StringBuilder.
        /// </summary>
        /// <param name="ipSeed">The IP of the HTTP request; this is used to avoid temp file collisions.</param>
        public PagedStringBuilder(string ipSeed)
        {
            if (!Directory.Exists("\\SD\\PagedCache")) Directory.CreateDirectory("\\SD\\PagedCache");
            path = "\\SD\\PagedCache\\" + Program.currentTime.ToString("yyyy_MM_dd_HH_mm_ss_") + ipSeed + "_" + new Random().Next(9999) + ".cache";
            fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            sw = new StreamWriter(fs);
        }
        public PagedStringBuilder Append(string value)
        {
            sw.Write(value);
            sw.Flush();
            return this;
        }
        public PagedStringBuilder Append(int value)
        {
            sw.Write(value);
            sw.Flush();
            return this;
        }
        public PagedStringBuilder Append(DateTime value)
        {
            sw.Write(value);
            sw.Flush();
            return this;
        }
        public PagedStringBuilder Append(float value)
        {
            sw.Write(value);
            sw.Flush();
            return this;
        }
        /// <summary>
        /// Returns the length/total number of bytes in the file.
        /// </summary>
        public int Length
        {
            get { return (int)fs.Length; }
        }
        /// <summary>
        /// This must be called before reading, in-order to reset the position of where we're reading from within the file.
        /// </summary>
        public void beginRead()
        {
            fs.Position = 0;
        }
        /// <summary>
        /// Reads from the paged-cache at the specified location and length; warning: you should recall this method with an offset of 0;
        /// to restart reading from the start of the file, invoke beginRead and specify an offset.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public int read(byte[] buffer, int offset, int length)
        {
            return fs.Read(buffer, offset, length);
        }
        /// <summary>
        /// Disposes the paged-memory.
        /// </summary>
        public void dispose()
        { // We use lots of try-catch's just in-case one fails...it's worth at least trying to delete the file, else this could cause storage issues
            try { sw.Dispose(); } catch { }
            try { fs.Close(); } catch { }
            try { fs.Dispose(); } catch { }
            try { File.Delete(path); } catch { throw new Exception("Failed to delete cache file '" + path + "'!"); }
        }
    }
}