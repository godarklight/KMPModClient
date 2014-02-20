THIS PROGRAM MUST RUN FROM YOUR KSP DIRECTORY  
THIS PROGRAM IS STILL EXPERIMENTAL  
  
This is a command line program to connect to KMP servers and sync your GameData folder.  
  
This will connect to the server straight away and use the default port of 2076:  
mono KMPModClient.exe server_ip  
  
This will connect to the server straight away:  
mono KMPModClient.exe username server_ip server_port  
  
This will ask for info after starting:  
mono KMPModClient.exe  
  
The program works like this:  
1. It connects to a KMP server and downloads KMPModControl.txt  
2. It backups up mods from GameData/ to KMPModClient/Mods/  
3a. If whitelisting, it then DELETES mods not listed in the required, optional or resource section from GameData  
3b. If blacklisting, it then DELETES mods listed in the resource section.  
4. It then copys missing mods listed from required, optional (and resource if whitelist) from KMPModClient/Mods/ to GameData/  
5. It then checks for any missing mods and then warns you.  
6. It then tries to fix up and SHA dependencies (Currently not supported).  
  
You will need to place any mods that you need into KMPModClient/Mods/ - However placing missing mods in GameData will also work thanks to the backup.  
