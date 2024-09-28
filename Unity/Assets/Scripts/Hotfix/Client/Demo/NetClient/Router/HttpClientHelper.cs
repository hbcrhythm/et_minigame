using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
#if UNITY_WEBGL
using UnityEngine.Networking;
#endif

namespace ET.Client
{
    public static partial class HttpClientHelper
    {
#if UNITY_WEBGL
    public static async ETTask<string> Get(string link)
    {
        try
        {
            using UnityWebRequest request = UnityWebRequest.Get(link);
            var operation = request.SendWebRequest();

            // 等待请求完成
            await AwaitRequest(operation);

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                throw new Exception($"HTTP request failed: {link}\n{request.error}");
            }
            Log.Info($" 完成 request.downloadHandler.text {request.downloadHandler.text}");
            return request.downloadHandler.text;
        }
        catch (Exception e)
        {
            throw new Exception($"HTTP request failed: {link}\n{e.Message}");
        }
    }

    private static ETTask AwaitRequest(UnityWebRequestAsyncOperation operation)
    {
        var tcs = ETTask.Create(true);
        operation.completed += _ => tcs.SetResult();
        return tcs;
    }

#else
        public static async ETTask<string> Get(string link)
        {
            try
            {
                using HttpClient httpClient = new();
                HttpResponseMessage response =  await httpClient.GetAsync(link);
                string result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (Exception e)
            {
                throw new Exception($"http request fail: {link.Substring(0,link.IndexOf('?'))}\n{e}");
            }
        }
#endif
    }
}