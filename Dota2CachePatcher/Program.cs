using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
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
        var config = VdfConvert.Deserialize(File.ReadAllText(Path.Combine(steamPath, "config", "libraryfolders.vdf")));
        Console.WriteLine(config.Value.GetType());
        foreach (var library in ((VObject)config.Value).Properties().Select(property => (VObject)property.Value))
        {
            if (library["apps"] is not VObject apps) continue;
            if (apps.Properties().All(app => app.Key != "570")) continue;
            if (library["path"] is not VValue path) throw new Exception("Failed to parse library config");
            return Path.Combine(path.Value<string>().Replace(@"\\", @"\"), "steamapps", "common", "dota 2 beta");
        }

        throw new FileNotFoundException("Dota 2 not found in library");
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