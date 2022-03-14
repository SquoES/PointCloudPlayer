using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BunnyCDN;
using BunnyCDN.Controller;
using PCStorage.Model;
using PointCloud.Player;
using UnityEngine;
using UnityEngine.Networking;

public static class VideoManager
{
    #region Built in videos

    private const string bi_fnCDNPreloadVideos = "system/preloadvideos.txt";

    #endregion

    #region CDN Videos

    private static List<string> cdn_Videos;
    internal static List<string> CDN_Videos => cdn_Videos;
    internal static async Task CDN_GetList(Action<bool> updateCompleted)
    {
        BunnyCDNUser bunny = new BunnyCDNUser();
        var videos = await bunny.GetVideosListAsync();
        bunny.Dispose();

        cdn_Videos = new List<string>();
        for (int i = 0; i < videos.Count; i++)
        {
            cdn_Videos.Add(videos[i].ObjectName);
        }
        updateCompleted.Invoke(true);
    }
    internal static async Task CDN_LoadVideo(string shortPath, IProgress<DoubleInt> progress)
    {
        BunnyCDNUser bunny = new BunnyCDNUser();
        //------Collect files
        List<StorageObject> videosList =
            await bunny.GetStorageObjectsAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/Frames");
        List<StorageObject> audiosList =
            await bunny.GetStorageObjectsAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/Audio");
        int totalCount = videosList.Count + audiosList.Count + 2;
        int currentLoaded = 0;
        string locPathFrame = Saver.CreatePath_CloudsBinaryFrames(shortPath);
        string locPathAudio = Saver.CreatePath_CloudsBinaryAudio(shortPath);
        
        //-----Load info file
        await bunny.DownloadObjectAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.fName_PCInfo}",
            Saver.CreatePath_CloudsBinaryInfo($"{shortPath}"));
        currentLoaded++;
        progress.Report(new DoubleInt(currentLoaded, totalCount));
        
        //-----Load preview image
        await bunny.DownloadObjectAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.fName_PCPreview}",
            Saver.CreatePath_CloudsBinaryPreview($"{shortPath}"));
        currentLoaded++;
        progress.Report(new DoubleInt(currentLoaded, totalCount));

        //-----Load frames
        Parallel.For(0, videosList.Count,  async i =>
        {
            await bunny.DownloadObjectAsync(
                $"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.dirName_Frames}/{videosList[i].ObjectName}",
                $"{locPathFrame}/{videosList[i].ObjectName}");
            currentLoaded ++;
            progress.Report(new DoubleInt(currentLoaded, totalCount));
        });

        //-----Load audios
        for (int i = 0; i < audiosList.Count; i++)
        {
            await bunny.DownloadObjectAsync(
                $"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.dirName_Audio}/{audiosList[i].ObjectName}",
                $"{locPathAudio}/{audiosList[i].ObjectName}");
            currentLoaded ++;
            progress.Report(new DoubleInt(currentLoaded, totalCount));
        }

        bunny.Dispose();
        progress.Report(new DoubleInt(totalCount, totalCount));
    }
    internal static async Task CDN_UpdateInfo(string shortPath)
    {
        BunnyCDNUser bunny = new BunnyCDNUser();
        await bunny.DownloadObjectAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.fName_PCInfo}",
            Saver.CreatePath_CloudsBinaryInfo($"{shortPath}"));
        bunny.Dispose();
    }
    internal static async Task CDN_LoadPreview(string shortPath)
    {
        BunnyCDNUser bunny = new BunnyCDNUser();
        await bunny.DownloadObjectAsync($"{BunnyCDNStorage.path_StudioVideos}/{shortPath}/{Saver.fName_PCPreview}",
            Saver.CreatePath_CloudsBinaryPreview($"{shortPath}"));
        bunny.Dispose();
    }

    #endregion

    #region Local videos

    private static List<string> local_Videos;
    internal static List<string> Local_Videos => local_Videos;

    internal static void Local_GetList()
    {
        string[] days = Saver.Binary_GetDays();

        local_Videos = new List<string>();
        for (int i = 0; i < days.Length; i++)
        {
            string[] circles = Saver.Binary_GetDayCircles(days[i]);
            if (circles == null || circles.Length < 1) continue;

            for (int j = 0; j < circles.Length; j++)
            {
                //TODO: HARDCODE HERE. Fix it
                if (circles[j].Contains("Frame") || circles[j].Contains("Aud")) continue;
                local_Videos.Add($"{days[i]}/{circles[j]}");
            }
        }
    }

    internal static void Local_Clear(string shortPath)
    {
        string fullPath = Saver.CreatePath_CloudsBinary(shortPath);
        if (!Directory.Exists(fullPath)) return;
        Directory.Delete(fullPath, true);
    }

    #endregion

    #region Video playeres controls

    private static CloudPlayer _currentPlayer;
    private static string _currentVideo;
    
    internal static void VPC_RegisterPlayer(CloudPlayer player)
    {
        _currentPlayer = player;
        VPC_PrepareVideoPlayer();
    }

    internal static void VPC_RegisterVideo(string shortPath)
    {
        _currentVideo = shortPath;
        VPC_PrepareVideoPlayer();
    }

    public static void VPC_ClearPlayer()
    {
        if (_currentPlayer != null)
        {
            _currentPlayer.Dispose();
            _currentPlayer = null;
        }
        _currentVideo = null;
    }

    private static void VPC_PrepareVideoPlayer()
    {
        if (_currentPlayer != null && _currentVideo != null)
        {
            _currentPlayer.Stop();
            _currentPlayer.Init(_currentVideo);
        }
    }

    #endregion
    
    #region Utils

    internal static async Task<PCInfo> GetVideoInfo(string shortPath)
    {
        bool exists = Saver.Binary_InfoExists(shortPath);
        if (!exists)
        {
            await CDN_UpdateInfo(shortPath);
        }
        string docPath = Saver.CreatePath_CloudsBinaryInfo(shortPath);
        string data = File.ReadAllText(docPath);
        if (string.IsNullOrEmpty(data))
        {
            await CDN_UpdateInfo(shortPath);
            data = File.ReadAllText(docPath);
            if (string.IsNullOrEmpty(data))
            {
                PCInfo pInfo = new PCInfo();
                pInfo.version = -2;
                return pInfo;
            }
        }
        PCInfo info = JsonUtility.FromJson<PCInfo>(data);
        return info;
    }
    internal static IEnumerator GetVideoPreview(string shortPath, Action<Sprite> result)
    {
        string fullPath = Saver.CreatePath_CloudsBinaryPreview(shortPath);
        bool exists = File.Exists(fullPath);
        if (!exists)
        {
            Task load = CDN_LoadPreview(shortPath);
            while (!load.IsCompleted)
            {
                yield return null;
            }
        }

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(String.Format("file:///{0}", fullPath)))
        {
            yield return request.SendWebRequest();
            Texture texture = DownloadHandlerTexture.GetContent(request);
            texture.name = shortPath;
            result.Invoke(texture.ToSprite());
        }
    }
    internal static async Task<bool> CheckLoaded(string shortPath)
    {
        //Info file exists
        bool exists = Saver.Binary_InfoExists(shortPath);
        if (!exists) return false;

        //Correct frames count
        PCInfo curInfo = Saver.Binary_GetInfo(shortPath);
        uint framesCount = (uint)Saver.Binary_GetFramesCount(shortPath);
        if (framesCount != curInfo.framesCount) return false;

        bool isLocal = shortPath.Contains("/") || shortPath.Contains("\\");
        if (isLocal) return true;
        //Update info file
        await CDN_UpdateInfo(shortPath);
        PCInfo newInfo = Saver.Binary_GetInfo(shortPath);
        if (newInfo.version != curInfo.version) return false;
        return true;
    }
    internal static IEnumerator GetAudio(string shortPath, Action<AudioClip> result)
    {
        string pathAudio = Saver.CreatePath_CloudsBinaryAudio(shortPath);
        string webPathAudio = String.Format("file:///{0}", pathAudio);
        string[] audioFiles = Saver.GetFiles(pathAudio);

        if (audioFiles == null || audioFiles.Length < 1)
        {
            result.Invoke(null);
            yield break;
        }
        
        string webPathFile = String.Format("{0}/{1}", webPathAudio, audioFiles[0]);
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(webPathFile, AudioType.UNKNOWN))
        {
            yield return request.SendWebRequest();
            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            clip.name = audioFiles[0];
            result.Invoke(clip);
        }
    }

    #endregion
}
