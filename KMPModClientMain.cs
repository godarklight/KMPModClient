//This is proof of concept code. Steals code from KMPChatClient, so automatically inherits GPLv3.
//This program assumes it is running from the KSP directory.
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace KMPModClient
{
	class KMPModClient
	{
		//KMPModCilent specfic things.
		static bool isInteractive = false;
		static bool missingmods = false;
		static bool shouldReceiveMessages;
		static byte[] modControlBytes;
		static string KSPPath;
		static Socket modTCPSocket;
		const int HANDSHAKE_ID = 0;
		//Copied from KerbalMultiPlayer
		public static Dictionary<string, SHAMod> modFileList = new Dictionary<string, SHAMod> ();
		public static List<string> resourceList = new List<string> ();
		public static string resourceControlMode = "blacklist";
		#region Main logic
		public static int Main (string[] args)
		{
			KSPPath = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly ().Location);
			if (!File.Exists (Path.Combine (KSPPath, "KSP.exe"))) {
				Console.WriteLine ("This program must be placed in the KSP directory next to KSP.exe");
				Console.WriteLine ("Press enter to exit");
				Console.ReadLine ();
				return 1;
			}
			SetupModControl ();
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
				if (ConnectToServer (address, port)) {
					Console.WriteLine ("Connected to " + address + " port " + port);
					Console.WriteLine ("Downloading Mod List");
					HandleConnection ();
					SyncGameDataFolder ();
					if (isInteractive || missingmods) {
						Console.WriteLine ("Press enter to exit");
						Console.ReadLine ();
					}


				} else {
					Console.WriteLine ("Failed to connect to server.");
					return 1;
				}
			} else {
				Console.WriteLine ("You must enter a server address and port number");
				return 1;
			}
			return 0;
		}
		#endregion
		#region Mod Control Setup
		private static void SetupModControl ()
		{
			//Make KMPModClient folder if needed
			CreateDirectoryIfNeeded (Path.Combine (KSPPath, "KMPModClient", "Mods"));
			CreateDirectoryIfNeeded (Path.Combine (KSPPath, "KMPModClient", "Mods"));
			CreateDirectoryIfNeeded (Path.Combine (KSPPath, "KMPModClient", "SHA"));
			CreateDirectoryIfNeeded (Path.Combine (KSPPath, "KMPModClient", "SHA", "SHA-Objects"));
			CreateFileIfNeeded (Path.Combine (KSPPath, "KMPModClient", "SHA", "SHA-Index.txt"));

		}

		private static void CreateDirectoryIfNeeded (string directory)
		{
			if (!Directory.Exists (directory)) {
				Console.WriteLine ("Creating " + directory);
				Directory.CreateDirectory (directory);
			}
		}

		private static void CreateFileIfNeeded (string directory)
		{
			if (!File.Exists (directory)) {
				Console.WriteLine ("Creating " + directory);
				File.Create (directory);
			}
		}
		#endregion
		#region Connection code
		private static bool ConnectToServer (string hostname, string port)
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

		private static void HandleConnection ()
		{
			shouldReceiveMessages = true;
			int bytes_to_receive = 8;
			int message_type = 0;
			bool header_received = false;
			byte[] receive_buffer = new byte[8];
			while (shouldReceiveMessages) {
				bytes_to_receive -= modTCPSocket.Receive (receive_buffer, receive_buffer.Length - bytes_to_receive, bytes_to_receive, SocketFlags.None);
				if (bytes_to_receive == 0) {
					if (header_received == false) {
						//We received the header
						message_type = BitConverter.ToInt32 (receive_buffer, 0);
						int message_size = BitConverter.ToInt32 (receive_buffer, 4);
						if (message_size != 0) {
							header_received = true;
							receive_buffer = new byte[message_size];
							bytes_to_receive = message_size;
						} else {
							ReceiveMessage (message_type, null);
							header_received = false;
							message_type = 0;
							bytes_to_receive = 8;
							receive_buffer = new byte[8];
						}
					} else {
						//We received the message data
						byte[] decompressedMessage = Decompress (receive_buffer);
						ReceiveMessage (message_type, decompressedMessage);
						header_received = false;
						message_type = 0;
						bytes_to_receive = 8;
						receive_buffer = new byte[8];
					}
				}
			}
			modTCPSocket.Close ();
		}

		private static void ReceiveMessage (int message_type, byte[] message_data)
		{
			if (message_type == HANDSHAKE_ID) {
				Console.WriteLine ("Handshake received: " + message_data.Length + " bytes.");
				shouldReceiveMessages = false;
				GetModControlFromHandshake (message_data);
			}
		}

		private static void GetModControlFromHandshake (byte[] message_data)
		{
			int server_version_length = BitConverter.ToInt32 (message_data, 4);
			int kmpModControl_length = BitConverter.ToInt32 (message_data, 20 + server_version_length);
			modControlBytes = new byte[kmpModControl_length];
			Array.Copy (message_data, 24 + server_version_length, modControlBytes, 0, kmpModControl_length);
			ParseModFile ();
		}
		#endregion
		#region Parse Mod File
		//Mostly copy pasted directly from KerbalMultiPlayer.
		private static void ParseModFile ()
		{
			string ModFileContent = System.Text.Encoding.UTF8.GetString (modControlBytes);
			using (System.IO.StringReader reader = new System.IO.StringReader(ModFileContent)) {
				string resourcemode = "whitelist";
				List<string> allowedParts = new List<string> ();
				Dictionary<string, SHAMod> hashes = new Dictionary<string, SHAMod> ();
				List<string> resources = new List<string> ();
				string line;
				string[] splitline = new string[2];
				string readmode = "";
				while (true) {
					line = reader.ReadLine (); //Trim off any whitespace from the start or end. This would allow indenting of the mod file.
					if (line == null) {
						break;
					}
					line = line.Trim ();
					try {
						if (!String.IsNullOrEmpty (line) && line [0] != '#') { //Skip empty or commented lines.
							if (line [0] == '!') { //changing readmode
								string trimmedLine = line.Substring (1); //Returns 'partslist' from ' !partslist'
								switch (trimmedLine) {
								case "partslist":
								case "required-files":
								case "optional-files":
									readmode = trimmedLine;
									break;
								case "resource-blacklist": //allow all resources EXCEPT these in file
									readmode = "resource";
									resourcemode = "blacklist";
									break;
								case "resource-whitelist": //allow NO resources EXCEPT these in file
									readmode = "resource";
									resourcemode = "whitelist";
									break;
								}
							} else {
								if (readmode == "partslist") {
									allowedParts.Add (line);
								}
								if (readmode == "required-files") {
									string hash = "";
									splitline [0] = line;
									if (line.Contains ("=")) { //Let's make the = on the end of the lines optional
										splitline = line.Split ('=');
										if (splitline.Length > 1) {
											hash = splitline [1];
										}
									}
									hashes.Add (splitline [0], new SHAMod {
										sha = hash,
										required = true
									});
								}
								if (readmode == "optional-files") {
									splitline = line.Split ('=');
									string hash = "";
									splitline [0] = line;
									if (line.Contains ("=")) { //Let's make the = on the end of the lines optional
										splitline = line.Split ('=');
										if (splitline.Length > 1) {
											hash = splitline [1];
										}
									}
									hashes.Add (splitline [0], new SHAMod {
										sha = hash,
										required = false
									});
								}
								if (readmode == "resource") {
									resources.Add (line);
								}
							}
						}
					} catch (Exception e) {
						Console.WriteLine (e.ToString ());
					}

				}
				//make all the vars global once we're done parsing
				modFileList = hashes;
				resourceControlMode = resourcemode;
				resourceList = resources;
			}
		}
		#endregion
		#region Sync GameData logic
		private static bool SyncGameDataFolder ()
		{
			BackupModsFromGameData ();
			BackupSHAObjects ();
			DeleteModsFromGameData ();
			CopyNeededModsToGameData ();
			CheckForMissingMods ();
			ReplaceModsWithSpecifiedShaVersion ();

			if (missingmods) {
				return false;
			} else {
				return true;
			}
		}

		private static void BackupModsFromGameData ()
		{   
			//Copy new mods from GameData to KMPModClient/Mods/
			string[] current_gamedata_folders = Directory.GetDirectories (Path.Combine (KSPPath, "GameData"));
			string[] current_backup_folders = Directory.GetDirectories (Path.Combine (KSPPath, "KMPModClient", "Mods"));
			foreach (string current_gamedata_folder in current_gamedata_folders) {
				//Remove the Fullpath and leading path seperator
				string stripped_gamedata_folder = current_gamedata_folder.Replace (Path.Combine (KSPPath, "GameData"), "").Remove (0, 1);
				bool copy_folder = true;
				//Don't back up KMP or Squad
				if (stripped_gamedata_folder.ToLowerInvariant () == "squad" || stripped_gamedata_folder.ToLowerInvariant () == "kmp" || stripped_gamedata_folder.ToLowerInvariant () == "000_toolbar") {
					copy_folder = false;
				}
				foreach (string current_backup_folder in current_backup_folders) {
					if (Directory.Exists (Path.Combine (KSPPath, "KMPModClient", "Mods", stripped_gamedata_folder))) {
						copy_folder = false;
					}
				}
				if (copy_folder) {
					Console.WriteLine ("Backing up mod: " + stripped_gamedata_folder);
					DirectoryCopy (current_gamedata_folder, Path.Combine (KSPPath, "KMPModClient", "Mods", stripped_gamedata_folder), true);
				}
			}
		}

		private static void BackupSHAObjects ()
		{
			//Not supported yet.
		}

		private static void DeleteModsFromGameData ()
		{
			string[] current_folders = Directory.GetDirectories (Path.Combine (KSPPath, "GameData"));
			//Delete everything not on the list
			foreach (string current_folder in current_folders) {
				bool deletefolder;
				string stripped_current_folder = current_folder.Replace (Path.Combine (KSPPath, "GameData"), "").Remove (0, 1);
				if (resourceControlMode == "whitelist") {
					//In whitelist mode, delete mods not part of required/optional or resource list.
					deletefolder = true;
					foreach (string modFile in modFileList.Keys) {
						if (modFile.StartsWith (stripped_current_folder)) {
							deletefolder = false;
						}
					}
					foreach (string resource in resourceList) {
						if (resource.StartsWith (stripped_current_folder)) {
							deletefolder = false;
						}
					}
				} else {
					//In blacklist mode, delete blacklisted mods.
					deletefolder = false;
					foreach (string resource in resourceList) {
						if (resource.StartsWith (stripped_current_folder)) {
							deletefolder = true;
						}
					}
				}
				//Don't delete KMP, Squad or Toolbar.
				if (stripped_current_folder.ToLowerInvariant () == "kmp" || stripped_current_folder.ToLowerInvariant () == "squad" || stripped_current_folder.ToLowerInvariant () == "000_toolbar") {
					deletefolder = false;
				}
				if (deletefolder) {
					Console.WriteLine ("Deleting: " + stripped_current_folder);
					Directory.Delete (current_folder, true);
				}
			}
		}

		private static void CopyNeededModsToGameData ()
		{
			string[] backup_folders = Directory.GetDirectories (Path.Combine (KSPPath, "KMPModClient", "Mods"));
			//Delete everything not on the list
			foreach (string backup_folder in backup_folders) {
				//Delete everything not on the list
				bool copyfolder;
				string stripped_backup_folder = backup_folder.Replace (Path.Combine (KSPPath, "KMPModClient", "Mods"), "").Remove (0, 1);
				if (resourceControlMode == "whitelist") {
					//In whitelist mode, copy mods in the required/optional or resource list.
					copyfolder = false;
					foreach (string modFile in modFileList.Keys) {
						if (modFile.StartsWith (stripped_backup_folder)) {
							copyfolder = true;
						}
					}
					foreach (string resource in resourceList) {
						if (resource.StartsWith (stripped_backup_folder)) {
							copyfolder = true;
						}
					}
				} else {
					//In blacklist mode, copy mods listed in required/optional.
					copyfolder = true;
					foreach (string modFile in modFileList.Keys) {
						if (modFile.StartsWith (stripped_backup_folder)) {
							copyfolder = false;
						}
					}
				}
				//Don't copy folders that already exist
				if (Directory.Exists (Path.Combine (KSPPath, "GameData", stripped_backup_folder))) {
					copyfolder = false;
				}
				//Don't copy KMP, Squad or Toolbar.
				if (stripped_backup_folder.ToLowerInvariant () == "kmp" || stripped_backup_folder.ToLowerInvariant () == "squad" || stripped_backup_folder.ToLowerInvariant () == "000_toolbar") {
					copyfolder = false;
				}
				if (copyfolder) {
					Console.WriteLine ("Installing: " + stripped_backup_folder);
					string varsource = Path.Combine (KSPPath, "KMPModClient", "Mods", stripped_backup_folder);
					string vardestination = Path.Combine (KSPPath, "GameData", stripped_backup_folder);
					DirectoryCopy (varsource, vardestination, true);
				}
			}
		}

		private static void ReplaceModsWithSpecifiedShaVersion ()
		{
			//Not supported yet.
		}
		#endregion
		#region Directory copy code
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

		private static void CheckForMissingMods ()
		{
			foreach (KeyValuePair <string,SHAMod> modFile in modFileList) {
				if (!File.Exists (Path.Combine (KSPPath, "GameData", modFile.Key))) {
					if (modFile.Value.required) {
						Console.WriteLine ("Missing required mod: " + modFile.Key);
						missingmods = true;
					} else {
						Console.WriteLine ("Missing optional mod: " + modFile.Key);
					}
				}
			}
		}
		#endregion
		#region Decompression code
		private static byte[] Decompress (byte[] data)
		{
			if (data == null) //Null data will cause the readers to throw
				return null;

			byte[] decompressedData = null;
			using (MemoryStream compressedInput = new MemoryStream(data)) {

				using (BinaryReader inputBinaryReader = new BinaryReader(compressedInput)) {
					bool isCompressed = inputBinaryReader.ReadBoolean ();

					if (!isCompressed) {
						//Data is not compressed: (false), (Real message)
						inputBinaryReader.Read (decompressedData, 0, decompressedData.Length - 1);
					} else {
						//Data is compressed: (true), int32 for decompressed size, (Real message)
						int decompressedSize = inputBinaryReader.ReadInt32 ();
						decompressedData = new byte[decompressedSize];
						using (GZipStream decompressionStream = new GZipStream(compressedInput, CompressionMode.Decompress)) {
							decompressionStream.Read (decompressedData, 0, decompressedSize);
						}
					}
				}
			}
			return decompressedData;

		}
		#endregion
	}
	//Copied directly from KerbalMultiPlayer
	public class SHAMod
	{
		public string sha { get; set; }

		public bool required { get; set; }
	}
}
