THIS PROGRAM IS PROOF OF CONCEPT - IT MUST RUN FROM YOUR KSP DIRECTORY

This is a command line program to connect to KMP servers and sync your GameData folder.

This will connect to the server straight away and use the default port of 2076:
mono KMPModClient.exe username server_ip

This will connect to the server straight away:
mono KMPModClient.exe username server_ip server_port

This will ask for info after starting:
mono KMPModClient.exe

The program works like this:
1. It connects to a KMP server and downloads KMPModControl.txt
2. It backups up mods from GameData to GameData-KMPModControl
3. It copys missing mods from GameData-KMPModControl to GameData (It will warn on missing mods)
4. It then DELETES mods not listed in the required section of KMPModControl.txt file from GameData

You will need to place any mods that you need into GameData-KMPModControl - However placing missing mods in GameData will also work thanks to the backup.
