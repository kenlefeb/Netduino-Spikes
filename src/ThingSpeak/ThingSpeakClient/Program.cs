/*
 ThingSpeak Client
 This program allows you to update a ThingSpeak Channel via the ThingSpeak API using a Netduino Plus

 Getting Started with ThingSpeak:

	* Sign Up for New User Account - https://www.thingspeak.com/users/new
	* Create a New Channel by selecting Channels and then Create New Channel
	* Enter the Write API Key in this program under "ThingSpeak Settings"

 @created: January 26, 2011
 @updated:

 @tutorial: http://community.thingspeak.com/tutorials/netduino/create-your-own-web-of-things-using-the-netduino-plus-and-thingspeak/

 @copyright (c) 2011 Hans Scharler (http://www.iamshadowlord.com)
 @licence: Creative Commons, Attribution-Share Alike 3.0 United States, http://creativecommons.org/licenses/by-sa/3.0/us/deed.en

 */

using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using Microsoft.SPOT.Net.NetworkInformation;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.NetduinoPlus;
using Socket = System.Net.Sockets.Socket;
using StringBuilder = System.Text.StringBuilder;

namespace ThingSpeakClient
{
	public class Program
	{

		//ThingSpeak Settings
		const string writeAPIKey = "TIHL4GH3NSKDZ6CH"; // Write API Key for a ThingSpeak Channel
		static string tsIP = "184.106.153.149";     // IP Address for the ThingSpeak API
		static Int32 tsPort = 80;                   // Port Number for ThingSpeak
		const int updateInterval = 3000;           // Time interval in milliseconds to update ThingSpeak

		static OutputPort led = new OutputPort(Pins.ONBOARD_LED, false);
		private static bool _active = false;

		public static void Main()
		{
			var button = new InterruptPort(Pins.ONBOARD_SW1, true, Port.ResistorMode.Disabled, Port.InterruptMode.InterruptEdgeBoth);
			button.OnInterrupt += new NativeEventHandler(Button_OnInterrupt);
			button.EnableInterrupt();

			while (true)
			{
				if (_active)
					UploadData(new AnalogInput(Cpu.AnalogChannel.ANALOG_2));
			}
		}

		private static void Button_OnInterrupt(uint data1, uint data2, DateTime time)
		{
			Debug.Print(time + ": " + data1 + ", " + data2);
			_active = !_active;
			var currentLed = led.Read();
			led.Write(false);
			for (var i = 0; i < 10; i++)
			{
				led.Write(true);
				led.Write(false);
			}
			led.Write(currentLed);
		}

		private static void UploadData(AnalogInput input)
		{
			while (_active)
			{
				delayLoop(updateInterval);

				// Check analog input on Pin A0
				double analogReading = input.Read();

				// Update the ThingSpeak Field with the value and if the light sensor value is above 500 also update your channel status
				if (analogReading >= 500)
				{
					updateThingSpeak("field1=" + analogReading.ToString() + "&status=Someone is in your room!!!");
				}
				else
				{
					updateThingSpeak("field1=" + analogReading.ToString());
				}
			}
		}

		static string FromByteArray(byte[] bytes)
		{
			var builder = new StringBuilder();
			foreach (var thisByte in bytes)
			{
				if (builder.Length > 0)
					builder.Append(":");
				var dec = string.Concat(thisByte);
				builder.Append(dec);
			}
			return builder.ToString();
		}

		static void updateThingSpeak(string tsData)
		{
			Debug.Print("Connected to ThingSpeak...\n");
			led.Write(true);

			var nics = NetworkInterface.GetAllNetworkInterfaces();
			foreach (var nic in nics)
			{
				Debug.Print(FromByteArray(nic.PhysicalAddress));
				var wifi = nic as Wireless80211;
				if (wifi != null)
				{
					wifi.Authentication = Wireless80211.AuthenticationType.None;
					wifi.Encryption = Wireless80211.EncryptionType.WPAPSK;
					wifi.PassPhrase = "2162020536";
					wifi.Radio = Wireless80211.RadioType.g;
					wifi.Ssid = "media.lefebvre.us";
					Wireless80211.ValidateConfiguration(wifi);
					//Wireless80211.SaveConfiguration(new Wireless80211[] {wifi}, false);
				}
				Debug.Print("Is network available? " + System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable());
				while (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) ;
				Debug.Print(nic.IPAddress);
				if (!nic.IsDhcpEnabled)
					continue;
				else
					break;
			}

			String request = "POST /update HTTP/1.1\n";
			request += "Host: " + tsIP + "\n";
			request += "THINGSPEAKAPIKEY: " + writeAPIKey + "\n";
			request += "Content-Length: " + tsData.Length + "\n\n";

			request += tsData;

			try
			{
				String tsReply = sendPOST(tsIP, tsPort, request);
				Debug.Print(tsReply);
				Debug.Print("...disconnected.\n");
				led.Write(false);
			}
			catch (SocketException se)
			{
				Debug.Print("Connection Failed.\n");
				Debug.Print("Socket Error Code: " + se.ErrorCode.ToString());
				Debug.Print(se.ToString());
				Debug.Print("\n");
				led.Write(false);
			}
		}

		// Issues a http POST request to the specified server. (From the .NET Micro Framework SDK example)
		private static String sendPOST(String server, Int32 port, String request)
		{
			const Int32 c_microsecondsPerSecond = 1000000;

			// Create a socket connection to the specified server and port.
			using (Socket serverSocket = ConnectSocket(server, port))
			{
				// Send request to the server.
				Byte[] bytesToSend = Encoding.UTF8.GetBytes(request);
				serverSocket.Send(bytesToSend, bytesToSend.Length, 0);

				// Reusable buffer for receiving chunks of the document.
				Byte[] buffer = new Byte[1024];

				// Accumulates the received page as it is built from the buffer.
				String page = String.Empty;

				// Wait up to 30 seconds for initial data to be available.  Throws an exception if the connection is closed with no data sent.
				DateTime timeoutAt = DateTime.Now.AddSeconds(30);
				while (serverSocket.Available == 0 && DateTime.Now < timeoutAt)
				{
					System.Threading.Thread.Sleep(100);
				}

				// Poll for data until 30-second timeout.  Returns true for data and connection closed.
				while (serverSocket.Poll(30 * c_microsecondsPerSecond, SelectMode.SelectRead))
				{
					// If there are 0 bytes in the buffer, then the connection is closed, or we have timed out.
					if (serverSocket.Available == 0) break;

					// Zero all bytes in the re-usable buffer.
					Array.Clear(buffer, 0, buffer.Length);

					// Read a buffer-sized HTML chunk.
					Int32 bytesRead = serverSocket.Receive(buffer);

					// Append the chunk to the string.
					page = page + new String(Encoding.UTF8.GetChars(buffer));
				}

				// Return the complete string.
				return page;
			}
		}

		// Creates a socket and uses the socket to connect to the server's IP address and port. (From the .NET Micro Framework SDK example)
		private static Socket ConnectSocket(String server, Int32 port)
		{
			// Get server's IP address.
			IPHostEntry hostEntry = Dns.GetHostEntry(server);

			// Create socket and connect to the server's IP address and port
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect(new IPEndPoint(hostEntry.AddressList[0], port));
			return socket;
		}

		static void delayLoop(int interval)
		{
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
			int offset = (int)(now % interval);
			int delay = interval - offset;
			Thread.Sleep(delay);
		}
	}
}