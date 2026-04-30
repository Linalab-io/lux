import assert from 'node:assert/strict';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import type { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { createServer } from '../src/server.js';
import {
  LUX_CONTEXT_COMMAND,
  LUX_CONTEXT_TOOL_NAME,
  LUX_EXECUTE_SHELL_COMMAND,
  LUX_EXECUTE_SHELL_TOOL_NAME,
  UNITY_SELECTED_FILE_CONTEXT_COMMAND,
  UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
} from '../src/unity-tcp-client.js';

interface ConnectedMcpPair {
  client: Client;
  server: Server;
  close(): Promise<void>;
}

interface LocalUnityTcpServer {
  port: number;
  requests: unknown[];
  close(): Promise<void>;
}

async function withTempProject(run: (projectRoot: string) => Promise<void>): Promise<void> {
  const projectRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-ai-bridge-mcp-project-'));
  fs.mkdirSync(path.join(projectRoot, 'Packages'), { recursive: true });
  fs.writeFileSync(path.join(projectRoot, 'Packages', 'manifest.json'), '{}\n', 'utf8');

  try {
    await run(projectRoot);
  } finally {
    fs.rmSync(projectRoot, { recursive: true, force: true });
  }
}

async function connectMcpServer(projectRoot: string): Promise<ConnectedMcpPair> {
  const server = createServer({ projectRoot });
  const client = new Client({ name: 'unity-ai-bridge-mcp-test', version: '0.1.0' }, { capabilities: {} });
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();

  await Promise.all([server.connect(serverTransport), client.connect(clientTransport)]);

  return {
    client,
    server,
    async close(): Promise<void> {
      await Promise.allSettled([client.close(), server.close()]);
    },
  };
}

function writeDiscovery(projectRoot: string, port: number, token = 'test-token'): void {
  const discoveryDirectory = path.join(projectRoot, 'Library', 'UnityAiBridge');
  fs.mkdirSync(discoveryDirectory, { recursive: true });
  fs.writeFileSync(
    path.join(discoveryDirectory, 'server.json'),
    JSON.stringify(
      {
        host: '127.0.0.1',
        port,
        token,
        protocolVersion: '1',
        projectPath: projectRoot,
        pid: process.pid,
        startedAtUtc: new Date(0).toISOString(),
      },
      null,
      2,
    ),
    'utf8',
  );
}

async function startLocalUnityTcpServer(response: unknown): Promise<LocalUnityTcpServer> {
  const requests: unknown[] = [];
  const server = net.createServer((socket) => {
    socket.setEncoding('utf8');
    let buffer = '';

    socket.on('data', (chunk) => {
      buffer += chunk;
      const newlineIndex = buffer.indexOf('\n');
      if (newlineIndex < 0) {
        return;
      }

      requests.push(JSON.parse(buffer.slice(0, newlineIndex)));
      socket.write(`${JSON.stringify(response)}\n`);
      socket.end();
    });
  });

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject);
      resolve();
    });
  });

  const address = server.address();
  assert.ok(address && typeof address === 'object');

  return {
    port: address.port,
    requests,
    close(): Promise<void> {
      return new Promise((resolve, reject) => {
        server.close((error) => {
          if (error) {
            reject(error);
            return;
          }

          resolve();
        });
      });
    },
  };
}

async function reserveClosedPort(): Promise<number> {
  const server = net.createServer();

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject);
      resolve();
    });
  });

  const address = server.address();
  assert.ok(address && typeof address === 'object');
  const port = address.port;

  await new Promise<void>((resolve, reject) => {
    server.close((error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });

  return port;
}

test('tools/list exposes Unity context and Lux tools', async () => {
  await withTempProject(async (projectRoot) => {
    const pair = await connectMcpServer(projectRoot);

    try {
      const result = await pair.client.listTools();

      const toolNames = result.tools.map((tool) => tool.name);
      assert.deepEqual(toolNames, [
        UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
        LUX_CONTEXT_TOOL_NAME,
        'lux_execute_shell',
        'lux_execute_git',
      ]);
      assert.equal(result.tools[0]?.inputSchema.type, 'object');
      assert.deepEqual(result.tools[0]?.inputSchema.properties, {});
    } finally {
      await pair.close();
    }
  });
});

test('tools/call sends one Lux context TCP command and returns context JSON text', async () => {
  await withTempProject(async (projectRoot) => {
    const luxContext = {
      packageName: 'com.linalab.lux',
      protocolSurface: 'ai-bridge-tcp',
      controlTransport: 'RTCDataChannel structured control RPC',
    };

    const unityTcpServer = await startLocalUnityTcpServer({
      schemaVersion: 1,
      requestId: 'response-id',
      ok: true,
      errorCode: null,
      errorMessage: null,
      payload: {
        ping: null,
        protocolInfo: null,
        selectedFileContext: null,
        luxContext,
        luxAutomationResult: null,
      },
      capturedAtUtc: '2026-04-28T00:00:00.0000000Z',
    });
    writeDiscovery(projectRoot, unityTcpServer.port);
    const pair = await connectMcpServer(projectRoot);

    try {
      const result = await pair.client.callTool({ name: LUX_CONTEXT_TOOL_NAME });
      const content = (result as { content: Array<{ type: string; text?: string }> }).content;

      assert.equal(content[0]?.text, JSON.stringify(luxContext));
      assert.equal(unityTcpServer.requests.length, 1);
      assert.deepEqual(unityTcpServer.requests[0], {
        schemaVersion: 1,
        requestId: (unityTcpServer.requests[0] as { requestId: string }).requestId,
        command: LUX_CONTEXT_COMMAND,
        token: 'test-token',
        params: {},
      });
    } finally {
      await pair.close();
      await unityTcpServer.close();
    }
  });
});

test('tools/call sends Lux shell automation parameters to Unity TCP', async () => {
  await withTempProject(async (projectRoot) => {
    const automationResult = {
      allowed: false,
      success: false,
      exitCode: -1,
      output: '',
      error: '',
      message: 'Command contains blocked token: sudo',
    };

    const unityTcpServer = await startLocalUnityTcpServer({
      schemaVersion: 1,
      requestId: 'response-id',
      ok: true,
      errorCode: null,
      errorMessage: null,
      payload: {
        ping: null,
        protocolInfo: null,
        selectedFileContext: null,
        luxContext: null,
        luxAutomationResult: automationResult,
      },
      capturedAtUtc: '2026-04-28T00:00:00.0000000Z',
    });
    writeDiscovery(projectRoot, unityTcpServer.port);
    const pair = await connectMcpServer(projectRoot);

    try {
      const result = await pair.client.callTool({
        name: LUX_EXECUTE_SHELL_TOOL_NAME,
        arguments: {
          commandText: 'sudo rm -rf /tmp/lux-test',
          workingDirectory: '/tmp',
          actor: 'test-agent',
          approvalGranted: false,
        },
      });
      const content = (result as { content: Array<{ type: string; text?: string }> }).content;

      assert.equal(content[0]?.text, JSON.stringify(automationResult));
      assert.equal(unityTcpServer.requests.length, 1);
      assert.deepEqual(unityTcpServer.requests[0], {
        schemaVersion: 1,
        requestId: (unityTcpServer.requests[0] as { requestId: string }).requestId,
        command: LUX_EXECUTE_SHELL_COMMAND,
        token: 'test-token',
        params: {
          commandText: 'sudo rm -rf /tmp/lux-test',
          workingDirectory: '/tmp',
          actor: 'test-agent',
          approvalGranted: false,
        },
      });
    } finally {
      await pair.close();
      await unityTcpServer.close();
    }
  });
});

test('tools/call returns a clear MCP error when discovery file is missing', async () => {
  await withTempProject(async (projectRoot) => {
    const pair = await connectMcpServer(projectRoot);

    try {
      await assert.rejects(
        () => pair.client.callTool({ name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME }),
        /discovery file not found/i,
      );
    } finally {
      await pair.close();
    }
  });
});

test('tools/call sends one selected-file TCP command and returns selected context JSON text', async () => {
  await withTempProject(async (projectRoot) => {
    const selectedFileContext = {
      projectName: 'Neon Glitch',
      projectPath: projectRoot,
      unityVersion: '6000.3.13f1',
      selectionCapturedAtUtc: '2026-04-28T00:00:00.0000000Z',
      selectionCount: 1,
      selectedFiles: [
        {
          assetPath: 'Assets/Test.asset',
          absolutePath: path.join(projectRoot, 'Assets', 'Test.asset'),
          guid: 'guid-1',
          name: 'Test',
          extension: '.asset',
          isFolder: false,
          exists: true,
          mainAssetType: 'UnityEngine.TextAsset',
          fileSizeBytes: 123,
          lastModifiedUtc: '2026-04-28T00:00:00.0000000Z',
          selectionIndex: 0,
          selectionCount: 1,
        },
      ],
    };

    const unityTcpServer = await startLocalUnityTcpServer({
      schemaVersion: 1,
      requestId: 'response-id',
      ok: true,
      errorCode: null,
      errorMessage: null,
      payload: {
        ping: null,
        protocolInfo: null,
        selectedFileContext,
      },
      capturedAtUtc: '2026-04-28T00:00:00.0000000Z',
    });
    writeDiscovery(projectRoot, unityTcpServer.port);
    const pair = await connectMcpServer(projectRoot);

    try {
      const result = await pair.client.callTool({ name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME });
      const content = (result as { content: Array<{ type: string; text?: string }> }).content;

      assert.equal(content.length, 1);
      assert.equal(content[0]?.type, 'text');
      assert.equal(content[0]?.text, JSON.stringify(selectedFileContext));
      assert.equal(unityTcpServer.requests.length, 1);
      assert.deepEqual(unityTcpServer.requests[0], {
        schemaVersion: 1,
        requestId: (unityTcpServer.requests[0] as { requestId: string }).requestId,
        command: UNITY_SELECTED_FILE_CONTEXT_COMMAND,
        token: 'test-token',
        params: {},
      });
      assert.equal(typeof (unityTcpServer.requests[0] as { requestId: unknown }).requestId, 'string');
    } finally {
      await pair.close();
      await unityTcpServer.close();
    }
  });
});

test('tools/call returns a clear MCP error when Unity reports unauthorized', async () => {
  await withTempProject(async (projectRoot) => {
    const unityTcpServer = await startLocalUnityTcpServer({
      schemaVersion: 1,
      requestId: 'response-id',
      ok: false,
      errorCode: 'unauthorized',
      errorMessage: 'Invalid token.',
      payload: null,
      capturedAtUtc: '2026-04-28T00:00:00.0000000Z',
    });
    writeDiscovery(projectRoot, unityTcpServer.port);
    const pair = await connectMcpServer(projectRoot);

    try {
      await assert.rejects(
        () => pair.client.callTool({ name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME }),
        /unauthorized|Invalid token/i,
      );
    } finally {
      await pair.close();
      await unityTcpServer.close();
    }
  });
});

test('tools/call returns a clear MCP error when the TCP connection is refused', async () => {
  await withTempProject(async (projectRoot) => {
    const port = await reserveClosedPort();
    writeDiscovery(projectRoot, port);
    const pair = await connectMcpServer(projectRoot);

    try {
      await assert.rejects(
        () => pair.client.callTool({ name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME }),
        /connection refused/i,
      );
    } finally {
      await pair.close();
    }
  });
});
