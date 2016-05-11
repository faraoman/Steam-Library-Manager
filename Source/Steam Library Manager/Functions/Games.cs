﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Steam_Library_Manager.Functions
{
    class Games
    {

        public static void AddNewGame(string acfPath, int appID, string appName, string installationPath, Definitions.Library Library, long sizeOnDisk, bool isCompressed, bool isSteamBackup = false)
        {
            try
            {
                if (Library.Games.ToList().Count(x => x.appID == appID && x.IsSteamBackup == true) > 0)
                    return;

                // Make a new definition for game
                Definitions.Game Game = new Definitions.Game();

                // Set game appID
                Game.appID = appID;

                // Set game name
                Game.appName = appName;

                Game.gameHeaderImage = $"http://cdn.akamai.steamstatic.com/steam/apps/{appID}/header.jpg";

                // Set acf name, appmanifest_107410.acf as example
                Game.acfName = $"appmanifest_{appID}.acf";

                // Set game acf path
                Game.fullAcfPath = new FileInfo(acfPath);

                // Set workshop acf name
                Game.workShopAcfName = $"appworkshop_{appID}.acf";

                Game.workShopAcfPath = new FileInfo(Path.Combine(Library.workshopPath.FullName, Game.workShopAcfName));

                // Set installation path
                DirectoryInfo testOldInstallations = new DirectoryInfo(installationPath);

                Game.installationPath = (testOldInstallations.Exists && !isSteamBackup) ? testOldInstallations : new DirectoryInfo(installationPath);

                Game.installedLibrary = Library;

                // Define it is an archive
                Game.IsCompressed = isCompressed;

                Game.IsSteamBackup = isSteamBackup;

                Game.compressedName = new FileInfo(Path.Combine(Game.installedLibrary.steamAppsPath.FullName, Game.appID + ".zip"));

                Game.commonPath = new DirectoryInfo(Path.Combine(Library.commonPath.FullName, installationPath));
                Game.downloadPath = new DirectoryInfo(Path.Combine(Library.downloadPath.FullName, installationPath));
                Game.workShopPath = new DirectoryInfo(Path.Combine(Library.workshopPath.FullName, "content", appID.ToString()));

                // If game do not have a folder in "common" directory and "downloading" directory then skip this game
                if (!Game.commonPath.Exists && !Game.downloadPath.Exists && !Game.IsCompressed)
                    return; // Do not add pre-loads to list

                fileSystem.Game gameFunctions = new fileSystem.Game();

                // If SizeOnDisk value from .ACF file is not equals to 0
                if (Properties.Settings.Default.gameSizeCalculationMethod != "ACF" && !isCompressed)
                {
                    List<FileSystemInfo> gameFiles = Game.getFileList();

                    Parallel.ForEach(gameFiles, file =>
                    {
                        Game.sizeOnDisk += (file as FileInfo).Length;
                    });
                }
                else if (isCompressed)
                {
                    // If user want us to get archive size from real uncompressed size
                    if (Properties.Settings.Default.archiveSizeCalculationMethod.StartsWith("Uncompressed"))
                    {
                        // Open archive to read
                        using (ZipArchive zip = ZipFile.OpenRead(Game.compressedName.FullName))
                        {
                            // For each file in archive
                            foreach (ZipArchiveEntry entry in zip.Entries)
                            {
                                // Add file size to sizeOnDisk
                                Game.sizeOnDisk += entry.Length;
                            }
                        }
                    }
                    else
                    {
                        // And set archive size as game size
                        Game.sizeOnDisk = fileSystem.getFileSize(Path.Combine(Game.installedLibrary.steamAppsPath.FullName, Game.appID + ".zip"));
                    }
                }
                else
                    // Else set game size to size in acf
                    Game.sizeOnDisk = sizeOnDisk;

                Game.prettyGameSize = fileSystem.FormatBytes(Game.sizeOnDisk);

                Application.Current.Dispatcher.Invoke(delegate
                {
                    Game.contextMenuItems = Game.generateRightClickMenuItems();
                }, System.Windows.Threading.DispatcherPriority.Normal);

                // Add our game details to global list
                Library.Games.Add(Game);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        public static void UpdateGameList(Definitions.Library Library)
        {
            try
            {
                if (!Library.steamAppsPath.Exists)
                    Library.steamAppsPath.Create();

                // Foreach *.acf file found in library
                foreach (string game in Directory.EnumerateFiles(Library.steamAppsPath.FullName, "*.acf", SearchOption.TopDirectoryOnly))
                {
                    // Define a new value and call KeyValue
                    Framework.KeyValue Key = new Framework.KeyValue();

                    // Read the *.acf file as text
                    Key.ReadFileAsText(game);

                    // If key doesn't contains a child (value in acf file)
                    if (Key.Children.Count == 0)
                        continue;

                    AddNewGame(game, Convert.ToInt32(Key["appID"].Value), !string.IsNullOrEmpty(Key["name"].Value) ? Key["name"].Value : Key["UserConfig"]["name"].Value, Key["installdir"].Value, Library, Convert.ToInt64(Key["SizeOnDisk"].Value), false);
                }

                // Do a loop for each *.zip file in library
                foreach (string gameArchive in Directory.EnumerateFiles(Library.steamAppsPath.FullName, "*.zip", SearchOption.TopDirectoryOnly))
                {
                    // Open archive for read
                    using (ZipArchive compressedArchive = ZipFile.OpenRead(gameArchive))
                    {
                        // For each file in opened archive
                        foreach (ZipArchiveEntry file in compressedArchive.Entries.Where(x => x.Name.Contains(".acf")))
                        {
                            // If it contains
                            // Define a KeyValue reader
                            Framework.KeyValue Key = new Framework.KeyValue();

                            // Open .acf file from archive as text
                            Key.ReadAsText(file.Open());

                            // If acf file has no children, skip this archive
                            if (Key.Children.Count == 0)
                                continue;

                            AddNewGame(file.FullName, Convert.ToInt32(Key["appID"].Value), !string.IsNullOrEmpty(Key["name"].Value) ? Key["name"].Value : Key["UserConfig"]["name"].Value, Key["installdir"].Value, Library, Convert.ToInt64(Key["SizeOnDisk"].Value), true);
                        }
                    }
                }

                // If library is backup library
                if (Library.Backup)
                {
                    foreach (string skuFile in Directory.EnumerateFiles(Library.fullPath, "*.sis", SearchOption.AllDirectories))
                    {
                        Framework.KeyValue Key = new Framework.KeyValue();

                        Key.ReadFileAsText(skuFile);

                        string[] name = System.Text.RegularExpressions.Regex.Split(Key["name"].Value, " and ");

                        int i = 0;
                        long gameSize = fileSystem.GetDirectorySize(new DirectoryInfo(skuFile).Parent.FullName, true);
                        foreach (Framework.KeyValue app in Key["apps"].Children)
                        {
                            AddNewGame(skuFile, Convert.ToInt32(app.Value), name[i], Path.GetDirectoryName(skuFile), Library, gameSize, false, true);

                            if (name.Count() > 1)
                                i++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        public static void UpdateMainForm(Definitions.Library Library, string Search = null)
        {
            try
            {
                Func<Definitions.Game, object> Sort = SLM.Settings.getSortingMethod();

                if (MainWindow.Accessor.gamePanel.Dispatcher.CheckAccess())
                {
                    MainWindow.Accessor.gamePanel.ItemsSource = ((string.IsNullOrEmpty(Search)) ? Library.Games.OrderBy(Sort) : Library.Games.Where(
                        y => y.appName.ToLowerInvariant().Contains(Search.ToLowerInvariant()) // Search by appName
                        || y.appID.ToString().Contains(Search) // Search by app ID
                        ).OrderBy(Sort)
                        );
                }
                else
                {
                    MainWindow.Accessor.gamePanel.Dispatcher.Invoke(delegate
                    {
                        UpdateMainForm(Library, Search);
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

    }
}
