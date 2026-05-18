using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// Unity → Python 미들웨어로 전달하는 MCP 서버 설정 페이로드.
    /// IMcpConfigComposer 가 장착 플러그인 + 자격증명을 합쳐 생성, IAiChatService.SendMcpConfigAsync 로 전송.
    /// 미들웨어는 이 페이로드를 받아 provider 의 set_mcp_config 핸들러로 전달한다.
    /// JsonUtility 호환.
    /// </summary>
    [Serializable]
    public class McpConfigPayload
    {
        public string agentId;
        public List<McpConfigServerEntry> servers = new();

        public bool IsEmpty => servers == null || servers.Count == 0;
    }

    /// <summary>
    /// 단일 MCP 서버 항목. env 는 자격증명이 치환된 실제 값.
    /// </summary>
    [Serializable]
    public class McpConfigServerEntry
    {
        public string name;            // pluginId
        public string transport;       // "stdio" / "sse" / "http"
        public string command;
        public List<string> args = new();
        public List<McpEnvEntry> env = new();   // McpServerSpec 의 McpEnvEntry 재사용
    }
}
