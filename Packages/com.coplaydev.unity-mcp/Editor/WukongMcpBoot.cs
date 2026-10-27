using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
[InitializeOnLoad]
public static class WukongMcpBoot
{
    static WukongMcpBoot() { EditorApplication.delayCall += Start; }
    public static async void Start()
    {
        Debug.Log("WUKONG_MCP_CONNECT_START");
        bool connected = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
        Debug.Log("WUKONG_MCP_CONNECT_RESULT " + connected);
    }
}
