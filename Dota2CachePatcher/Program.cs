using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Win32;

namespace Dota2CachePatcher;

internal static class Program
{
    private static void Main()
    {
        var steamPath = GetSteamPath();
        Console.WriteLine($"Steam path is {steamPath}");
        var dota2Path = GetDota2Path(steamPath);
        Console.WriteLine($"Dota 2 path is {dota2Path}");
        foreach (var sharedObjectCachePath in GetSharedObjectCachePaths(dota2Path))
        {
            PatchSharedObjectCache(sharedObjectCachePath);
            Console.WriteLine($"Patched {sharedObjectCachePath}");
        }
    }

    [Pure]
    private static string GetSteamPath()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null) is string steamPath)
        {
            return steamPath;
        }

        throw new FileNotFoundException("Registry key not found");
    }


    [Pure]
    private static string GetDota2Path(string steamPath)
    {
        string libraryPath;
        {
            var config = File.ReadAllText(Path.Combine(steamPath, "config", "libraryfolders.vdf"));
            var match = Regex.Match(
                config,
                "{[\\s\n]+?\"path\"\\s+?\"(.+?)\".+?\"apps\"[\\s\n]+?{[\\s\n]+?\"570\".+?}.+?}",
                RegexOptions.Singleline);
            if (!match.Success)
            {
                throw new FileNotFoundException("Dota 2 not found in library");
            }

            libraryPath = match.Groups[1].Value.Replace(@"\\", @"\");
        }
        return Path.Combine(libraryPath, "steamapps", "common", "dota 2 beta");
    }

    [Pure]
    private static IEnumerable<string> GetSharedObjectCachePaths(string dota2Path)
    {
        var matcher = new Matcher();
        matcher.AddInclude("cache_*.soc");
        return matcher.GetResultsInFullPath(Path.Combine(dota2Path, "game", "dota"));
    }

    private static void PatchSharedObjectCache(string sharedObjectCachePath)
    {
        CMsgSerializedSOCache serializedCache;
        using (var stream = File.OpenRead(sharedObjectCachePath))
        {
            serializedCache = CMsgSerializedSOCache.Parser.ParseFrom(stream);
        }

        foreach (var genericCache in serializedCache.Caches)
        {
            foreach (var typeCache in genericCache.TypeCaches)
            {
                switch (typeCache.Type)
                {
                    case 2002:
                    {
                        var instance = CSODOTAGameAccountClient.Parser.ParseFrom(typeCache.Objects[0]);
                        instance.CompetitiveRank = 13370;
                        typeCache.Objects[0] = instance.ToByteString();
                        break;
                    }
                    case 2012:
                    {
                        var instance = CSODOTAGameAccountPlus.Parser.ParseFrom(typeCache.Objects[0]);
                        instance.PlusStatus = 1;
                        typeCache.Objects[0] = instance.ToByteString();
                        break;
                    }
                }
            }
        }

        using (var stream = File.OpenWrite(sharedObjectCachePath))
        {
            serializedCache.WriteTo(stream);
        }
    }
}