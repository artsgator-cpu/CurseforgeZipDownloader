using System.IO.Compression;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

namespace CurseforgeZipDownloader
{
    internal class Program
    {
        const string AdditionalFolder = "additional_files";

        static async Task Main(string[] args)
        {
            var cwd = Directory.GetCurrentDirectory();
            Console.WriteLine($"Searching zip file in \"{cwd}\".");

            var files = Directory.GetFiles(cwd).Select(x => new FileInfo(x)).ToList();
            var zipFiles = files.Where(x => x.Extension == ".zip").ToList();

            if (zipFiles.Count == 0)
            {
                Console.WriteLine($"Found no zip file in \"{cwd}\". Exiting.");
                Environment.Exit(0);
            }
            if (zipFiles.Count > 1)
            {
                Console.WriteLine($"Found multiple zip files in \"{cwd}\". Exiting.");
                Environment.Exit(0);
            }

            var zipPath = zipFiles.First().FullName;
            var folderPath = zipFiles.First().FullName.Remove(zipPath.Length - 4, 4);
            
            if (Directory.Exists(folderPath))
                AskDelete(folderPath);

            Console.WriteLine($"Unzipping to \"{folderPath}\"");
            ZipFile.ExtractToDirectory(zipPath, folderPath);

            var manifestfile = Path.Combine(folderPath, "manifest.json");
            var json = File.ReadAllText(manifestfile);
            var manifest = JsonConvert.DeserializeObject<Manifest>(json);

            if (manifest == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unexpected Error: Could not convert manifest file \"{manifestfile}\". Exiting.");
                Console.ResetColor();
                Environment.Exit(0);
            }

            var modInfos = manifest.files;
            var modUrls = new string[modInfos.Count];

            for (int i = 0; i < modInfos.Count; i++)
            {
                modUrls[i] = $"https://www.curseforge.com/api/v1/mods/{modInfos[i].projectID}/files/{modInfos[i].fileID}";
            }

            var modDir = Path.Combine(cwd, "mods");

            if (Directory.Exists(modDir))
                AskDelete(modDir);

            Directory.CreateDirectory(modDir);
            Directory.SetCurrentDirectory(modDir);

            using var client = new HttpClient();
            for (int i = 0; i < modUrls.Length; i++)
            {
                Console.Write($"Downloading mod {i + 1}/{modUrls.Length}: ");

                var response = await client.GetAsync(modUrls[i]);
                var data = await response.Content.ReadAsStringAsync();

                var filenameMatch = Regex.Match(data, @"""fileName""\:""(?<filename>[^""]+)""");
                var filename = filenameMatch.Groups["filename"].Value;

                Console.WriteLine(filename);

                using var s = await client.GetStreamAsync(modUrls[i] + "/download");
                using var fs = new FileStream(filename, FileMode.OpenOrCreate);
                await s.CopyToAsync(fs);
            }

            Console.WriteLine($"\nNOTE: The folder \"{folderPath}\\overrides\" may contain config files, ressource packs, datapacks, etc. which are used by the modpack.");

            var filesNotJar = Directory.GetFiles(modDir).Select(f => new FileInfo(f)).Where(fi => fi.Extension != ".jar").ToList();
            if (filesNotJar.Count > 0)
            {
                Directory.CreateDirectory(AdditionalFolder);
                Console.WriteLine($"\nNOTE: Files that are not .jar mods have been downloaded from the modlist. " +
                    $"They could be texturepacks or datapacks. They will be moved to the \"{AdditionalFolder}\" folder inside of the mod folder:");
                foreach (var file in filesNotJar)
                {
                    Console.WriteLine($"{filesNotJar.IndexOf(file) + 1}. {file.Name}");
                    file.MoveTo(AdditionalFolder + $"\\{file.Name}");
                }
            }
        }

        static void AskDelete(string dir)
        {
            Console.Write($"Directory \"{dir}\" already exists.\nDelete? (y/n): ");
            var ans = Console.ReadLine() ?? string.Empty;
            if (ans.StartsWith('y'))
                Directory.Delete(dir, true);
            else
            {
                Console.WriteLine("Exiting.");
                Environment.Exit(0);
            }
        }
    }
}
