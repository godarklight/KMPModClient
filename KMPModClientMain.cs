//This is proof of concept code. Steals code from KMPChatClient, so automatically inherits GPLv3.
//This program assumes it is running from the KSP directory.
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace KMPModClient
{
	class MainClass
	{
		static bool isInteractive = false;
		static bool missingmods = false;
		static bool shouldReceiveMessages;
		static string KSPPath;
		static Socket modTCPSocket;
		static byte[] receive_buffer = new byte[8192];
		const int HANDSHAKE_ID = 0;
		static List<string> modList = new List<string> ();

		public static int Main (string[] args)
		{
			KSPPath = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly ().Location);
			if (!File.Exists (KSPPath + "/KSP.exe")) {
				Console.WriteLine ("This program must be placed in the KSP directory next to KSP.exe");
				Console.WriteLine ("Press enter to exit");
				Console.ReadLine ();
				return 1;
			}
			string address = "";
			string port = "";
			if (args.Length == 0) {
				isInteractive = true;
				Console.WriteLine ("You can also run this program with arguments: KMPModClient [host] [port]");
				Console.WriteLine ("Type the IP of the KMP Server: ");
				address = Console.ReadLine ();
				Console.WriteLine ("Type the Port of the KMP Server (blank for 2076): ");
				port = Console.ReadLine ();
				if (String.IsNullOrEmpty (port)) {
					port = "2076";
				}
			}

			if (args.Length == 1) {
				address = args [0];
				port = "2076";
			}

			if (args.Length == 2) {
				address = args [0];
				port = args [1];
			}
			if (!String.IsNullOrEmpty (address) && !String.IsNullOrEmpty (port)) {
				Console.WriteLine ("Attempting to connect to " + address + " port " + port);
				if (connectToServer (address, port)) {
					Console.WriteLine ("Connected to " + address + " port " + port);
					Console.WriteLine ("Downloading Mod List");
					handleConnection ();
					if (isInteractive || missingmods) {
						Console.WriteLine ("Press enter to exit");
						Console.ReadLine ();
					}


				} else {
					Console.WriteLine ("Failed to connect to server.");
					return 1;
				}
			}
			return 0;
		}

		private static bool connectToServer (string hostname, string port)
		{
			IPAddress address = null;
			try {
				address = IPAddress.Parse (hostname);
			} catch (Exception) {
			}

			if (address == null) {
				try {
					address = Dns.GetHostAddresses (hostname) [0];
				} catch (Exception) {
				}
			}

			if (address != null) {
				IPEndPoint endpoint = new IPEndPoint (address, Convert.ToInt32 (port));
				TcpClient modTCPClient = new TcpClient ();

				modTCPClient.Connect (endpoint);
				modTCPSocket = modTCPClient.Client;
				if (modTCPSocket.Connected) {
					return true;
				}
			}
			return false;
		}

		private static void handleConnection ()
		{
			shouldReceiveMessages = true;
			int received_buffer_length = 0;
			int received_message_index = 0;
			int message_type = 0;
			int message_size = 0;
			bool header_received = false;
			while (shouldReceiveMessages) {
				//Get the header
				if (header_received == false) {
					received_buffer_length = modTCPSocket.Receive (receive_buffer, 8 - received_message_index, SocketFlags.None);
					received_message_index = received_message_index + received_buffer_length;
					if (received_message_index == 8) {
						message_type = BitConverter.ToInt32 (receive_buffer, 0);
						message_size = BitConverter.ToInt32 (receive_buffer, 4);
						if (message_size != 0) {
							header_received = true;
						}
						received_buffer_length = 0;
						received_message_index = 0;
					}
				}
				//Get the message
				if (header_received == true) {
					byte[] received_message = new byte[message_size];
					while (received_message_index < message_size) {
						int bytes_to_receive = Math.Min (receive_buffer.Length, message_size - received_message_index);
						received_buffer_length = modTCPSocket.Receive (receive_buffer, bytes_to_receive, SocketFlags.None);
						Array.Copy (receive_buffer, 0, received_message, received_message_index, received_buffer_length);
						received_message_index = received_message_index + received_buffer_length;
						if (received_message_index == message_size) {
							byte[] decompressedMessage = Decompress (received_message);
							receiveMessage (message_type, decompressedMessage);
							header_received = false;
							received_buffer_length = 0;
							received_message_index = 0;
							message_type = 0;
							message_size = 0;
						}
					}
				}
			}
			modTCPSocket.Shutdown (SocketShutdown.Both);
			modTCPSocket.Close ();
		}

		private static void receiveMessage (int message_type, byte[] message_data)
		{
			if (message_type == HANDSHAKE_ID) {
				shouldReceiveMessages = false;
				//Easiest way to do it is hand it down. I'm too lazy to do things in the main thread.
				parseModControl (getModControlFromHandshake (message_data));
			}
		}

		private static byte[] getModControlFromHandshake (byte[] message_data)
		{
			int server_version_length = BitConverter.ToInt32 (message_data, 4);
			int kmpModControl_length = BitConverter.ToInt32 (message_data, 16 + server_version_length);
			byte[] kmpModControl_bytes = new byte[kmpModControl_length];
			Array.Copy (message_data, 20 + server_version_length, kmpModControl_bytes, 0, kmpModControl_length);
			return kmpModControl_bytes;
		}

		private static void parseModControl (byte[] mod_data)
		{
			string mod_data_text = Encoding.UTF8.GetString (mod_data);
			StringReader reader = new StringReader (mod_data_text);
			string resourcemode = "blacklist";
			List<string> allowedParts = new List<string> ();
			Dictionary<string, string> hashes = new Dictionary<string, string> ();
			List<string> resources = new List<string> ();

			string line;
			string[] splitline;
			string readmode = "parts";

			while (reader.Peek() != -1) {

				line = reader.ReadLine ();
				if (! String.IsNullOrEmpty (line)) {
					if (line [0] != '#') {//allows commented lines
						if (line [0] == '!') {//changing readmode
							if (line.Contains ("partslist")) {
								readmode = "parts";
							} else if (line.Contains ("md5")) {
								readmode = "md5";
							} else if (line.Contains ("resource-blacklist")) { //allow all resources EXCEPT these in file
								readmode = "resource";
								resourcemode = "blacklist";
							} else if (line.Contains ("resource-whitelist")) { //allow NO resources EXCEPT these in file
								readmode = "resource";
								resourcemode = "whitelist";
							} else if (line.Contains ("required")) {
								readmode = "required";
							}
						} else if (readmode == "parts") {
							allowedParts.Add (line);
						} else if (readmode == "md5") {
							splitline = line.Split ('=');
							hashes.Add (splitline [0], splitline [1]); //stores path:md5
						} else if (readmode == "resource") {
							resources.Add (line);
						} else if (readmode == "required") {
							modList.Add (line);
						}
					}
				}
			}
			if (resourcemode == "I hate compiler warnings" && resourcemode == "I really hate compiler warnings") //I can't really be bothered to work around this.
				Console.WriteLine ("End the universe, your computer just went full retard");

			syncGameDataFolder (modList);
			reader.Close ();
		}

		private static bool syncGameDataFolder (List<string> required_folders)
		{
			//Make GameData-KMPModControl folder if needed
			if (!Directory.Exists (KSPPath + "/GameData-KMPModControl"))
				Directory.CreateDirectory (KSPPath + "/GameData-KMPModControl");

			//Backup everything not saved in GameData-KMPModControl
			string[] current_gamedata_folders = Directory.GetDirectories (KSPPath + "/GameData/");
			string[] current_backup_folders = Directory.GetDirectories (KSPPath + "/GameData-KMPModControl/");
			foreach (string current_gamedata_folder in current_gamedata_folders) {
				string stripped_gamedata_folder = current_gamedata_folder.Replace (KSPPath + "/GameData/", "");
				bool copy_folder = true;
				//Don't back up KMP or Squad
				if (stripped_gamedata_folder == "Squad" || stripped_gamedata_folder == "KMP") {
					copy_folder = false;
				}
				foreach (string current_backup_folder in current_backup_folders) {
					if (Directory.Exists (KSPPath + "/GameData-KMPModControl/" + stripped_gamedata_folder))
						copy_folder = false;
				}
				if (copy_folder) {
					Console.WriteLine ("Backing up mod: " + stripped_gamedata_folder);
					DirectoryCopy (current_gamedata_folder, KSPPath + "/GameData-KMPModControl/" + stripped_gamedata_folder, true);
				}
			}

			//Copy needed mods to GameData
			foreach (string required_folder in required_folders) {
				if (required_folder != "KMP" && required_folder != "Squad") {
					if (!Directory.Exists (KSPPath + "/GameData/" + required_folder)) {
						if (Directory.Exists (KSPPath + "/GameData-KMPModControl/" + required_folder)) {
							string varsource = KSPPath + "/GameData-KMPModControl/" + required_folder;
							string vardestination = KSPPath + "/GameData/" + required_folder;
							DirectoryCopy (varsource, vardestination, true);
							Console.WriteLine ("Installing " + required_folder);
						} else {
							Console.WriteLine ("Missing: " + required_folder + " from GameData-KMPModControl");
							missingmods = true;
						}
					}
				}
			}
			string[] current_folders = Directory.GetDirectories (KSPPath + "/GameData/");
			//Delete everything not on the list
			foreach (string current_folder in current_folders) {
				//Don't delete KMP or Squad
				if (current_folder != KSPPath + "/GameData/KMP" && current_folder != KSPPath + "/GameData/Squad") {
					bool deletefolder = true;
					foreach (string required_folder in required_folders) {
						if (KSPPath + "/GameData/" + required_folder == current_folder) {
							deletefolder = false;
						}
					}
					if (deletefolder == true) {
						Console.WriteLine ("Deleting: " + current_folder);
						Directory.Delete (current_folder, true);
					}

				}
			}
			if (missingmods) {
				return false;
			} else {
				return true;
			}
		}
		//Shamelessly stolen from the MSDN website. C# does not have a directory copy method...
		private static void DirectoryCopy (string sourceDirName, string destDirName, bool copySubDirs)
		{
			// Get the subdirectories for the specified directory.
			DirectoryInfo dir = new DirectoryInfo (sourceDirName);
			DirectoryInfo[] dirs = dir.GetDirectories ();

			if (!dir.Exists) {
				throw new DirectoryNotFoundException (
					"Source directory does not exist or could not be found: "
					+ sourceDirName);
			}

			// If the destination directory doesn't exist, create it. 
			if (!Directory.Exists (destDirName)) {
				Directory.CreateDirectory (destDirName);
			}

			// Get the files in the directory and copy them to the new location.
			FileInfo[] files = dir.GetFiles ();
			foreach (FileInfo file in files) {
				string temppath = Path.Combine (destDirName, file.Name);
				file.CopyTo (temppath, false);
			}

			// If copying subdirectories, copy them and their contents to new location. 
			if (copySubDirs) {
				foreach (DirectoryInfo subdir in dirs) {
					string temppath = Path.Combine (destDirName, subdir.Name);
					DirectoryCopy (subdir.FullName, temppath, copySubDirs);
				}
			}
		}

		private static byte[] Decompress (byte[] data)
		{
			if (data == null) //Null data will cause the readers to throw
				return null;

			byte[] decompressedData = null;
			MemoryStream compressedInput = new MemoryStream (data);

			BinaryReader inputBinaryReader = new BinaryReader (compressedInput);
			bool isCompressed = inputBinaryReader.ReadBoolean ();

			if (!isCompressed) {
				//Data is not compressed: (false), (Real message)
				inputBinaryReader.Read (decompressedData, 0, decompressedData.Length - 1);
			} else {
				//Data is compressed: (true), int32 for decompressed size, (Real message)
				int decompressedSize = inputBinaryReader.ReadInt32 ();
				decompressedData = new byte[decompressedSize];
				GZipStream decompressionStream = new GZipStream (compressedInput, CompressionMode.Decompress);
				decompressionStream.Read (decompressedData, 0, decompressedSize);
				decompressionStream.Close ();
			}
			inputBinaryReader.Close ();
			compressedInput.Close ();
			return decompressedData;

		}
	}
}
