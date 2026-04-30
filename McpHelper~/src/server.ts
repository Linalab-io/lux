import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ErrorCode, ListToolsRequestSchema, McpError } from '@modelcontextprotocol/sdk/types.js';
import { logToStderr } from './logger.js';
import { findUnityProjectRoot } from './project-root.js';
import {
  executeLuxGitInUnity,
  executeLuxShellInUnity,
  getLuxContextFromUnity,
  getSelectedFileContextFromUnity,
  LUX_CONTEXT_TOOL_NAME,
  LUX_EXECUTE_GIT_TOOL_NAME,
  LUX_EXECUTE_SHELL_TOOL_NAME,
  type LuxAutomationRequest,
  UnityTcpContextError,
  UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
} from './unity-tcp-client.js';

const SERVER_NAME = 'linalab-unity-ai-bridge';
const SERVER_VERSION = '0.1.0';

export interface HelperServerConfig {
  projectRoot?: string;
}

export function createServer(config: HelperServerConfig = {}): Server {
  const server = new Server(
    {
      name: SERVER_NAME,
      version: SERVER_VERSION,
    },
    {
      capabilities: {
        tools: {},
      },
    },
  );

  server.setRequestHandler(ListToolsRequestSchema, () => ({
    tools: [
      {
        name: UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME,
        title: 'Unity Selected File Context',
        description: 'Returns Unity editor metadata for the currently selected project files without reading file contents.',
        inputSchema: {
          type: 'object',
          properties: {},
          required: [],
          additionalProperties: false,
        },
        annotations: {
          readOnlyHint: true,
          destructiveHint: false,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_CONTEXT_TOOL_NAME,
        title: 'Lux Context',
        description: 'Returns Lux package, remote gateway, and automation policy metadata from the Unity editor.',
        inputSchema: {
          type: 'object',
          properties: {},
          required: [],
          additionalProperties: false,
        },
        annotations: {
          readOnlyHint: true,
          destructiveHint: false,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_EXECUTE_SHELL_TOOL_NAME,
        title: 'Lux Execute Shell',
        description: 'Runs a shell command through Lux automation policy and returns the audited result.',
        inputSchema: automationInputSchema(['commandText']),
        annotations: {
          readOnlyHint: false,
          destructiveHint: true,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
      {
        name: LUX_EXECUTE_GIT_TOOL_NAME,
        title: 'Lux Execute Git',
        description: 'Runs git arguments through Lux automation policy and returns the audited result.',
        inputSchema: automationInputSchema(['gitArguments']),
        annotations: {
          readOnlyHint: false,
          destructiveHint: true,
          idempotentHint: false,
          openWorldHint: false,
        },
      },
    ],
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const toolName = request.params.name;
    if (!isKnownTool(toolName)) {
      throw new McpError(ErrorCode.InvalidParams, `Unknown tool: ${request.params.name}`);
    }

    const projectRoot = config.projectRoot ?? findUnityProjectRoot();
    if (!projectRoot) {
      throw new McpError(ErrorCode.InvalidRequest, 'Unity project root not found. Set UNITY_PROJECT_PATH or run the MCP helper inside a Unity project.');
    }

    try {
      const payload = await callUnityTool(projectRoot, toolName, request.params.arguments);

      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(payload),
          },
        ],
      };
    } catch (error) {
      if (error instanceof UnityTcpContextError) {
        throw new McpError(ErrorCode.InternalError, error.message, {
          code: error.code,
          ...toDataObject(error.data),
        });
      }

      throw error;
    }
  });

  return server;
}

function isKnownTool(toolName: string): boolean {
  return toolName === UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME
    || toolName === LUX_CONTEXT_TOOL_NAME
    || toolName === LUX_EXECUTE_SHELL_TOOL_NAME
    || toolName === LUX_EXECUTE_GIT_TOOL_NAME;
}

async function callUnityTool(projectRoot: string, toolName: string, argumentsValue: unknown): Promise<unknown> {
  switch (toolName) {
    case UNITY_SELECTED_FILE_CONTEXT_TOOL_NAME:
      return getSelectedFileContextFromUnity(projectRoot);
    case LUX_CONTEXT_TOOL_NAME:
      return getLuxContextFromUnity(projectRoot);
    case LUX_EXECUTE_SHELL_TOOL_NAME:
      return executeLuxShellInUnity(projectRoot, parseAutomationArguments(argumentsValue, 'commandText'));
    case LUX_EXECUTE_GIT_TOOL_NAME:
      return executeLuxGitInUnity(projectRoot, parseAutomationArguments(argumentsValue, 'gitArguments'));
    default:
      throw new McpError(ErrorCode.InvalidParams, `Unknown tool: ${toolName}`);
  }
}

function parseAutomationArguments(argumentsValue: unknown, requiredField: 'commandText' | 'gitArguments'): LuxAutomationRequest {
  if (!isPlainObject(argumentsValue)) {
    throw new McpError(ErrorCode.InvalidParams, 'Lux automation arguments must be an object.');
  }

  const requiredValue = argumentsValue[requiredField];
  if (typeof requiredValue !== 'string' || requiredValue.trim().length === 0) {
    throw new McpError(ErrorCode.InvalidParams, `${requiredField} is required.`);
  }

  return {
    commandText: optionalString(argumentsValue.commandText),
    gitArguments: optionalString(argumentsValue.gitArguments),
    workingDirectory: optionalString(argumentsValue.workingDirectory),
    actor: optionalString(argumentsValue.actor),
    approvalGranted: typeof argumentsValue.approvalGranted === 'boolean' ? argumentsValue.approvalGranted : false,
  };
}

function automationInputSchema(required: Array<'commandText' | 'gitArguments'>): Record<string, unknown> {
  return {
    type: 'object',
    properties: {
      commandText: { type: 'string' },
      gitArguments: { type: 'string' },
      workingDirectory: { type: 'string' },
      actor: { type: 'string' },
      approvalGranted: { type: 'boolean' },
    },
    required,
    additionalProperties: false,
  };
}

function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function toDataObject(data: unknown): Record<string, unknown> {
  return typeof data === 'object' && data !== null ? data as Record<string, unknown> : {};
}

export async function startServer(): Promise<void> {
  const projectRoot = findUnityProjectRoot();

  if (projectRoot) {
    logToStderr(`Unity project root: ${projectRoot}`);
  } else {
    logToStderr('Unity project root not found; continuing with MCP stdio scaffold only.');
  }

  const server = createServer({ projectRoot });
  const transport = new StdioServerTransport();
  await server.connect(transport);
}
