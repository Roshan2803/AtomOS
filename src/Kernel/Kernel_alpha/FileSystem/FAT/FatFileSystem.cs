﻿using System;
using System.Collections.Generic;
using Kernel_alpha.Drivers;
using Kernel_alpha.FileSystem.FAT;
using Kernel_alpha.Lib.Encoding;
using Kernel_alpha.FileSystem.FAT.Lists;

namespace Kernel_alpha.FileSystem
{
    public class FatFileSystem : GenericFileSystem
    {   
        protected UInt32 BytePerSector;
        protected UInt32 SectorsPerCluster;
        protected UInt32 ReservedSector;
        protected UInt32 TotalFAT;
        protected UInt32 DirectoryEntry;
        protected UInt32 TotalSectors;
        protected UInt32 SectorsPerFAT;
        protected UInt32 DataSectorCount;
        protected UInt32 ClusterCount;
        protected FatType FatType;
        protected UInt32 SerialNo;
        protected UInt32 RootCluster;
        protected UInt32 RootSector;
        protected UInt32 RootSectorCount;
        protected UInt32 DataSector;
        protected UInt32 EntriesPerSector;
        protected UInt32 fatEntries;
      //  protected string VolumeLabel;  
        protected List<FileSystem.FAT.Lists.Base> xRootDirFiles = new List<FileSystem.FAT.Lists.Base>();
        private Directory dir;
        private File file;
        protected string FileType;
        protected UInt16 DIR_CrtTime; 
        protected UInt16 DIR_CrtDate;  
        protected UInt16 DIR_WrtTime;  
        protected UInt16 DIR_WrtDate;  
        protected UInt32 DIR_FileSize;
        protected UInt16 Dir_StartCluster; 
        protected UInt16 Dir_Attribute;    
        protected UInt64 FatCurrentDirectorySector;

        public FatFileSystem(BlockDevice aDevice)
        {
            this.IDevice = aDevice;
            this.mIsValid = IsFAT();
        }

        private bool IsFAT()
        {
            var BootSector = new byte[512];
            this.IDevice.Read(0U, 1U, BootSector);

            var xSig = BitConverter.ToUInt16(BootSector, 510);
            if (xSig != 0xAA55)
                return false;

            /* BPB (BIOS Parameter Block) */
            BytePerSector = BitConverter.ToUInt16(BootSector, 11);
            SectorsPerCluster = BootSector[13];
            ReservedSector = BitConverter.ToUInt16(BootSector, 14);
            TotalFAT = BootSector[16];
            DirectoryEntry = BitConverter.ToUInt16(BootSector, 17);

            if (BitConverter.ToUInt16(BootSector, 19) == 0)
            {
                /* Large amount of sector on media. This field is set if there are more than 65535 sectors in the volume. */
                TotalSectors = BitConverter.ToUInt32(BootSector, 32);
            }
            else
            {
                TotalSectors = BitConverter.ToUInt16(BootSector, 19);
            }

            /* FAT 12 and FAT 16 ONLY */
            SectorsPerFAT = BitConverter.ToUInt16(BootSector, 22);

            if (SectorsPerFAT == 0)
            {
                /* FAT 32 ONLY */
                SectorsPerFAT = BitConverter.ToUInt32(BootSector, 36);
            }

            /* Not Necessary, To Avoid Crashes during corrupted BPB Info */
            //Just to prevent ourself from hacking
            if (TotalFAT == 0 || TotalFAT > 2 || BytePerSector == 0 || TotalSectors == 0)
                return false;

            /* Some basic calculations to check basic error :P */
            try
            {
                uint RootDirSectors = 0;
                DataSectorCount = TotalSectors - (ReservedSector + (TotalFAT * SectorsPerFAT) + RootDirSectors);
                ClusterCount = DataSectorCount / SectorsPerCluster;
            }
            catch
            {
                return false;
            }

            /* Finally we got key xD */
            if (ClusterCount < 4085)
                FatType = FatType.FAT12;
            else if (ClusterCount < 65525)
                FatType = FatType.FAT16;
            else
                FatType = FatType.FAT32;

            /* Now we open door of gold coins xDD */
            if (FatType == FatType.FAT32)
            {
                SerialNo = BitConverter.ToUInt32(BootSector, 39);
              //  VolumeLabel = ASCII.GetString(BootSector, 71, 11);   // for checking
              //  Console.WriteLine(VolumeLabel);
                RootCluster = BitConverter.ToUInt32(BootSector, 44);
                RootSector = 0;
                RootSectorCount = 0;
            }
            /* The key is of another door */
            else
            {
                SerialNo = BitConverter.ToUInt32(BootSector, 67);
                //VolumeLabel = ASCII.GetString(BootSector, 43, 11);
                RootSector = ReservedSector + (TotalFAT * SectorsPerFAT);
                RootSectorCount = (UInt32)((DirectoryEntry * 32 + (BytePerSector - 1)) / BytePerSector);
                fatEntries = SectorsPerFAT * 512 / 4;
            }
            /* Now it shows our forward path ;) */
            EntriesPerSector = (UInt32)(BytePerSector / 32);
            DataSector = ReservedSector + (TotalFAT * SectorsPerFAT) + RootSectorCount;
            return true;
        }

        public void ReadRootDir()
        {
            UInt32 xSector ;
            byte[] xdata = new byte[(512 * SectorsPerCluster)];
            xSector = DataSector + ((RootCluster - 2) * SectorsPerCluster);
            FatCurrentDirectorySector = xSector;   
            ReadCluster(xSector, xdata);
            DisplayDir(xRootDirFiles);
        }

       private void ReadCluster(UInt32 xSector, byte[] aData)
        {
            UInt32 File_offset;
            byte[] xdirData = new byte[32];
            string rootDirName = string.Empty;
            string rootDirExt = String.Empty ;
            string tempModifiedDate;
            this.IDevice.Read(xSector, SectorsPerCluster, aData);   
            Array.Copy(aData, 0, xdirData, 0, 31); 
            File_offset = 00;
            
            for (int xSecArray = 32; xSecArray < 1024; xSecArray += 32)
            {
                if (xdirData[0] == 00)
                    break;
                else
                {
                    // increamenting value by 31
                   File_offset = (UInt16)xSecArray;
                   Array.Copy(aData, xSecArray, xdirData, 0, 31);       
                 
                    if (xdirData[11] == 0x10)
                        FileType = "directory"; //IsDirectory = true; // 
                    else if (xdirData[11] == 0x20)
                        FileType = "file";
                    else
                        continue;

                    if (xdirData[0] != 00 && xdirData[0] != 229)
                    {
                        // dir exists
                        if (xdirData[11] != 15)
                        {
                            // not a long file name
                            byte[] VDirName = new byte[8];
                            byte[] VDirExt = new byte[3];

                            for (int i = 0; i < 11; i++)
                            {
                                if (i < 8)
                                    VDirName[i] = xdirData[i];
                                else
                                    VDirExt[i - 8] = xdirData[i];
                            }

                            rootDirName = ASCII.GetString(VDirName, 0, VDirName.Length);
                            if (FileType != "directory")
                                rootDirExt = "." + ASCII.GetString(VDirName, 0, VDirName.Length);
                            rootDirName = rootDirName.Trim(' ') + rootDirExt.Trim(' ');   // HAVE TO TRIM - SANDEEP
                         
                             Console.Write(rootDirName + "    ");
                        }

                        Dir_Attribute = xdirData[(int)Entry.FileAttributes];
                        DIR_CrtTime = BitConverter.ToUInt16(xdirData, (int)Entry.CreationTime);
                        DIR_CrtDate = BitConverter.ToUInt16(xdirData, (int)Entry.CreationDate);
                        DIR_WrtTime = BitConverter.ToUInt16(xdirData, (int)Entry.LastModifiedTime);
                        DIR_WrtDate = BitConverter.ToUInt16(xdirData, (int)Entry.LastModifiedDate);
                        DIR_FileSize = BitConverter.ToUInt32(xdirData, (int)Entry.FileSize);
                        Dir_StartCluster = BitConverter.ToUInt16(xdirData, (int)Entry.FirstCluster);

                        // tempModifiedDate has to be changed -- SANDEEP
                     //   tempModifiedDate = (ushort)(((DIR_WrtDate & 0x1F))) + "/" + (ushort)((DIR_WrtDate >> 5) & 0x0F) + "/" + (ushort)((DIR_WrtDate >> 9) + 1980);
                        tempModifiedDate = (DIR_WrtDate & 0x1F).ToString() +  "/" + ((DIR_WrtDate >> 5) & 0x0F).ToString() + "/" + ((DIR_WrtDate >> 9) + 1980).ToString();
                        if (FileType == "file")
                        {
                             xRootDirFiles.Add(file = new File(rootDirName, DIR_FileSize, tempModifiedDate));
                        }
                        else
                        {
                            xRootDirFiles.Add(dir = new Directory(rootDirName, tempModifiedDate));
                        }
                    }
               }
            }
        }

       
        public static void DisplayDir(List<FileSystem.FAT.Lists.Base> xListing1)
        {
            int filecount = 0;
            int dircount = 0;
            for (int a = 0; a < xListing1.Count; a++)
            {
                var xItem = xListing1[a];
                if (xItem is FileSystem.FAT.Lists.Directory)
                {
                    dircount++;
                    Console.WriteLine(xListing1[a].ModifiedDate + "\t" + "<DIR> " + "\t\t" + xListing1[a].Name);
                }
                else if (xItem is FileSystem.FAT.Lists.File)
                {
                    filecount++;
                    string[] tempstring = xListing1[a].Name.Split('.');
                     Console.WriteLine(xListing1[a].ModifiedDate + "\t" + "<FILE>" + "\t" + xListing1[a].Size.ToString() + "\t" + tempstring[0].Trim() + "." + tempstring[1].Trim());
                }
            }
            Console.WriteLine();
            Console.WriteLine("\t   " + filecount.ToString() + " " + "File(s)");
            Console.WriteLine("\t   " + dircount.ToString() + " " + "Dir(s)");
        }

       
        public void FlushDetails()
        {
            if (IsValid)
            {
                Console.WriteLine("FAT Version:" + ((FatType == FatType.FAT32) ? "FAT32" : "FAT16/12"));                
                //Console.WriteLine("Disk Volume:" + (VolumeLabel == "NO NAME" ? VolumeLabel + "<Extended>" : VolumeLabel));
                Console.WriteLine("Bytes Per Sector:" + BytePerSector.ToString());
                Console.WriteLine("Sectors Per Cluster:" + SectorsPerCluster.ToString());
                Console.WriteLine("Reserved Sector:" + ReservedSector.ToString());
                Console.WriteLine("Total FAT:" + TotalFAT.ToString());
                Console.WriteLine("Direactory Entry:" + DirectoryEntry.ToString());
                Console.WriteLine("Total Sectors:" + TotalSectors.ToString());
                Console.WriteLine("Sectors Per FAT:" + SectorsPerFAT.ToString());
                Console.WriteLine("Data Sector Count:" + DataSectorCount.ToString());
                Console.WriteLine("Cluster Count:" + ClusterCount.ToString());
                Console.WriteLine("Serial Number:" + SerialNo.ToString());
                Console.WriteLine("Root Cluster:" + RootCluster.ToString());
                Console.WriteLine("Root Sector:" + RootSector.ToString());
                Console.WriteLine("Root Sector Count:" + RootSectorCount.ToString());
                Console.WriteLine("Data Sector:" + DataSector.ToString());
            }
            else
                Console.WriteLine("No fat available");
        }
    }
}
